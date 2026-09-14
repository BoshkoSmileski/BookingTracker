namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// Asks SQL Server what the schema actually is, rather than asking EF what it
/// thinks the schema should be.
///
/// That distinction is the reason this file exists: <c>DbContext</c> and the
/// model snapshot are both derived from the same C# model, so comparing one to
/// the other cannot detect a migration that failed to bring a real database into
/// line with it. Every assertion about the post-migration schema in this suite
/// reads <c>sys.columns</c> / <c>sys.indexes</c> / <c>sys.foreign_keys</c>
/// directly.
///
/// Read-only throughout, and pointed at the throwaway database by
/// <see cref="MigrationTestDatabase.ConnectionString"/>, which refuses to name
/// any other.
///
/// **The queries themselves live in <see cref="SqlSchemaReader"/>.** They were
/// lifted out when the rollback suite needed the identical questions asked of
/// its own database: two copies of "how do you tell whether a column is
/// nvarchar(max)" is one copy too many. This class is the part that was never
/// shareable - the decision that these reads go to
/// <see cref="MigrationTestDatabase"/> and nowhere else - and its public surface
/// is unchanged, so every existing call site is untouched.
/// </summary>
public static class SchemaProbe
{
    public sealed record ColumnInfo(string Type, int MaxLength, bool IsNullable)
    {
        public int MaxCharacters => MaxLength < 0 ? -1 : (Type.StartsWith('n') ? MaxLength / 2 : MaxLength);
    }

    public sealed record IndexInfo(string Name, bool IsUnique, string? Filter, IReadOnlyList<string> KeyColumns, IReadOnlyList<string> IncludedColumns);

    /// <summary>
    /// Resolved per call rather than cached, so the isolation check in
    /// <see cref="MigrationTestDatabase.ConnectionString"/> is the thing that
    /// decides which database is opened - here as everywhere else in this suite.
    /// </summary>
    private static SqlSchemaReader Reader => new(MigrationTestDatabase.ConnectionString);

    public static Task<bool> TableExistsAsync(string table)
        => Reader.TableExistsAsync(table);

    public static async Task<ColumnInfo?> ColumnAsync(string table, string column)
        => await Reader.ColumnAsync(table, column) is { } c
            ? new ColumnInfo(c.Type, c.MaxLength, c.IsNullable)
            : null;

    public static async Task<IndexInfo?> IndexAsync(string table, string index)
        => await Reader.IndexAsync(table, index) is { } i
            ? new IndexInfo(i.Name, i.IsUnique, i.Filter, i.KeyColumns, i.IncludedColumns)
            : null;

    public static Task<bool> ForeignKeyCascadesOnDeleteAsync(string table, string foreignKey)
        => Reader.ForeignKeyCascadesOnDeleteAsync(table, foreignKey);

    /// <summary>Migration ids EF has recorded as applied, oldest first.</summary>
    public static Task<IReadOnlyList<string>> AppliedMigrationsAsync()
        => Reader.AppliedMigrationsAsync();

    public static Task<int> RowCountAsync(string table)
        => Reader.RowCountAsync(table);

    public static Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        => Reader.ScalarAsync<T>(sql, parameters);
}
