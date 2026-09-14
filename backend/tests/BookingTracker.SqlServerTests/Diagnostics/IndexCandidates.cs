using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// Applies a candidate index shape to the throwaway test database so it can be
/// measured before anything is committed to a migration.
///
/// Raw DDL rather than an EF migration, deliberately and only here: the point of
/// The point is to decide WHETHER an index is justified, and writing the
/// migration first would be the decision. Once one is chosen it goes through
/// <c>dotnet ef migrations add</c> like every other schema change in this
/// repository - nothing in production or in the model
/// snapshot is touched by this file.
///
/// Both candidates REPLACE an existing index rather than adding a third. That is
/// not incidental: BookingSessions already carries five indexes, the two
/// relevant ones already lead with the right columns, and each is short of
/// exactly one column that the conflict query needs. Adding a fourth overlapping
/// index would pay the write cost twice for the same read.
/// </summary>
public static class IndexCandidates
{
    public sealed record Candidate(string Name, string Description, string Drop, string Create, string Restore);

    /// <summary>
    /// Add BookingPageId to the index that already covers the WHERE clause.
    ///
    /// The seek on (Status, SelectedDate) already finds the right rows; the only
    /// reason SQL Server reaches for a second access path is that this index
    /// cannot supply the join column. As an INCLUDE rather than a key column
    /// because nothing seeks or orders by it - it is only projected.
    /// </summary>
    public static readonly Candidate CoverTheJoinColumn = new(
        "A: (Status, SelectedDate, SelectedTime) INCLUDE (BookingPageId)",
        "Covers the query from one seek. Range lock spans every organizer's bookings on the date.",
        Drop: "DROP INDEX [IX_BookingSessions_Status_SelectedDate_SelectedTime] ON [BookingSessions];",
        Create: """
            CREATE INDEX [IX_BookingSessions_Status_SelectedDate_SelectedTime]
            ON [BookingSessions] ([Status], [SelectedDate], [SelectedTime])
            INCLUDE ([BookingPageId]);
            """,
        Restore: """
            DROP INDEX [IX_BookingSessions_Status_SelectedDate_SelectedTime] ON [BookingSessions];
            CREATE INDEX [IX_BookingSessions_Status_SelectedDate_SelectedTime]
            ON [BookingSessions] ([Status], [SelectedDate], [SelectedTime]);
            """);

    /// <summary>
    /// Add SelectedDate to the index that already leads with the join column.
    ///
    /// The one that can narrow the LOCK rather than only the read: seeking per
    /// booking page means the key range covered belongs to one organizer's page,
    /// so two organizers booking the same date never touch the same keys.
    /// Depends on the optimizer choosing a nested loop driven by BookingPages -
    /// which is the thing to verify rather than assume.
    /// </summary>
    public static readonly Candidate SeekPerBookingPage = new(
        "B: (BookingPageId, Status, SelectedDate) INCLUDE (SelectedTime)",
        "Seeks once per booking page. Range lock spans one page's bookings on the date.",
        Drop: "DROP INDEX [IX_BookingSessions_BookingPageId_Status] ON [BookingSessions];",
        Create: """
            CREATE INDEX [IX_BookingSessions_BookingPageId_Status]
            ON [BookingSessions] ([BookingPageId], [Status], [SelectedDate])
            INCLUDE ([SelectedTime]);
            """,
        Restore: """
            DROP INDEX [IX_BookingSessions_BookingPageId_Status] ON [BookingSessions];
            CREATE INDEX [IX_BookingSessions_BookingPageId_Status]
            ON [BookingSessions] ([BookingPageId], [Status]);
            """);

    public static IReadOnlyList<Candidate> All => [CoverTheJoinColumn, SeekPerBookingPage];

    public static Task ApplyAsync(Candidate candidate) => ExecuteAsync(candidate.Drop + "\n" + candidate.Create);

    public static Task RevertAsync(Candidate candidate) => ExecuteAsync(candidate.Restore);

    /// <summary>Index size in pages, so the read win can be weighed against what it costs to store.</summary>
    public static async Task<IReadOnlyList<string>> SizesAsync(string table)
    {
        SqlServerTestDatabase.AssertIsolated();

        await using var connection = new SqlConnection(SqlServerTestDatabase.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT i.name, SUM(p.used_page_count) AS Pages, SUM(p.row_count) AS Rows
            FROM sys.dm_db_partition_stats AS p
            JOIN sys.indexes AS i ON i.object_id = p.object_id AND i.index_id = p.index_id
            WHERE p.object_id = OBJECT_ID(@table)
            GROUP BY i.name
            ORDER BY i.name;
            """, connection);
        command.Parameters.AddWithValue("@table", table);

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add($"  {reader.GetString(0),-58} {reader.GetInt64(1),6} pages  {reader.GetInt64(2),8} rows");
        }
        return rows;
    }

    private static async Task ExecuteAsync(string sql)
    {
        SqlServerTestDatabase.AssertIsolated();

        await using var connection = new SqlConnection(SqlServerTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }
}
