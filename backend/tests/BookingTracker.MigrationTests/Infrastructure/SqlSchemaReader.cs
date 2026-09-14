using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// Reads what a SQL Server database's schema ACTUALLY is, against whichever
/// database it is handed.
///
/// This is <see cref="SchemaProbe"/>'s body, lifted out so a second suite can ask
/// the same questions of a second database. <c>SchemaProbe</c> is pinned to
/// <see cref="MigrationTestDatabase"/> - correctly, because that is the one
/// database the correctness suite is allowed to touch - and the rollback suite
/// needs the identical queries pointed at its own. Copying them would be two
/// copies of "how do you tell whether a column is nvarchar(max)" to keep in step;
/// this is one copy with a connection string.
///
/// The distinction <c>SchemaProbe</c> documents still holds and is the whole
/// reason both exist: <c>DbContext</c> and the model snapshot are both derived
/// from the same C#, so comparing one to the other cannot detect a migration that
/// failed to bring a real database into line with either. Everything here reads
/// <c>sys.columns</c> / <c>sys.indexes</c> / <c>sys.foreign_keys</c> directly.
///
/// Read-only throughout. It performs no isolation check of its own on purpose -
/// the caller's database guard is the one place that decides which database may
/// be opened, and a second, weaker check here would be a second answer to that
/// question.
/// </summary>
public sealed class SqlSchemaReader(string connectionString)
{
    public sealed record ColumnInfo(string Type, int MaxLength, bool IsNullable)
    {
        /// <summary>
        /// <c>sys.columns.max_length</c> is in BYTES, so an nvarchar column
        /// reports twice its declared character length and nvarchar(max)
        /// reports -1. This is the character length a migration declares.
        /// </summary>
        public int MaxCharacters => MaxLength < 0 ? -1 : (Type.StartsWith('n') ? MaxLength / 2 : MaxLength);
    }

    public sealed record IndexInfo(
        string Name, bool IsUnique, string? Filter,
        IReadOnlyList<string> KeyColumns, IReadOnlyList<string> IncludedColumns);

    public async Task<bool> TableExistsAsync(string table)
        => await ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = @table", ("@table", table)) == 1;

    public async Task<ColumnInfo?> ColumnAsync(string table, string column)
    {
        const string sql = """
            SELECT t.name, c.max_length, c.is_nullable
            FROM sys.columns AS c
            JOIN sys.types   AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@table) AND c.name = @column;
            """;

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new ColumnInfo(reader.GetString(0), reader.GetInt16(1), reader.GetBoolean(2));
    }

    public async Task<IndexInfo?> IndexAsync(string table, string index)
    {
        const string sql = """
            SELECT i.is_unique, i.filter_definition
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(@table) AND i.name = @index;
            """;

        await using var connection = await OpenAsync();

        bool isUnique;
        string? filter;
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@index", index);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            isUnique = reader.GetBoolean(0);
            filter = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        // is_included_column separates the key from the INCLUDE list, which is
        // exactly the distinction NarrowBookingConflictLockFootprint is about -
        // an included column keeps the index covering without widening the key
        // (and therefore without widening the range lock).
        const string columnsSql = """
            SELECT c.name, ic.is_included_column
            FROM sys.index_columns AS ic
            JOIN sys.indexes AS i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@table) AND i.name = @index
            ORDER BY ic.is_included_column, ic.key_ordinal, c.name;
            """;

        var keys = new List<string>();
        var included = new List<string>();
        await using (var command = new SqlCommand(columnsSql, connection))
        {
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@index", index);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                (reader.GetBoolean(1) ? included : keys).Add(reader.GetString(0));
            }
        }

        return new IndexInfo(index, isUnique, filter, keys, included);
    }

    public async Task<bool> ForeignKeyCascadesOnDeleteAsync(string table, string foreignKey)
        => await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID(@table) AND name = @name AND delete_referential_action = 1;
            """,
            ("@table", table), ("@name", foreignKey)) == 1;

    /// <summary>Every foreign key on <paramref name="table"/>, by name.</summary>
    public async Task<IReadOnlyList<string>> ForeignKeysAsync(string table)
    {
        var names = new List<string>();

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            "SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@table) ORDER BY name;", connection);
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        return names;
    }

    /// <summary>
    /// Foreign keys whose child rows no longer have a parent.
    ///
    /// SQL Server enforces this continuously, so a non-empty answer would mean an
    /// FK was dropped and recreated without validation (WITH NOCHECK) rather than
    /// that a row escaped. Asked after every rollback step anyway, because "the
    /// constraint still exists" and "the data still satisfies it" are two claims
    /// and only one of them is about the data.
    /// </summary>
    public async Task<int> OrphanedChildRowsAsync(string childTable, string childColumn, string parentTable)
        => await ScalarAsync<int>($"""
            SELECT COUNT(*) FROM [{childTable}] AS c
            WHERE c.[{childColumn}] IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM [{parentTable}] AS p WHERE p.[Id] = c.[{childColumn}]);
            """);

    /// <summary>Migration ids EF has recorded as applied, oldest first.</summary>
    public async Task<IReadOnlyList<string>> AppliedMigrationsAsync()
    {
        var applied = new List<string>();

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) applied.Add(reader.GetString(0));

        return applied;
    }

    /// <summary>Every user table in the database, so a rollback's shape can be compared as a set.</summary>
    public async Task<IReadOnlyList<string>> TablesAsync()
    {
        var tables = new List<string>();

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            "SELECT name FROM sys.tables WHERE name <> '__EFMigrationsHistory' ORDER BY name;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        return tables;
    }

    public async Task<int> RowCountAsync(string table)
        => await ScalarAsync<int>($"SELECT COUNT(*) FROM [{table}];");

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T))!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
