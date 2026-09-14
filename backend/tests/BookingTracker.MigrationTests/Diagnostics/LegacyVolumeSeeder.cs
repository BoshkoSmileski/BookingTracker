using System.Data;
using System.Diagnostics;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// Fills a database standing at <see cref="LegacySchema.Baseline"/> with a
/// <see cref="VolumeProfile"/>'s worth of rows an older version of BookingTracker
/// would have written.
///
/// <b>Raw SQL and SqlBulkCopy, and that is forced rather than chosen</b> - the
/// same constraint <c>LegacyData</c> documents, for the same reason.
/// <c>BookingTrackerDbContext</c> IS the current model, so asking it to insert an
/// AvailabilityException emits an INSERT naming <c>EndDate</c>, and a
/// BookingSession one naming <c>MeetingProvider</c> and <c>MeetingUrl</c> -
/// three columns the legacy schema does not have. There is no context that
/// matches the baseline, and building one would be a second model to keep in step
/// with the migrations.
///
/// <b>Why not reuse <c>LegacyData</c> itself.</b> It is deliberately a
/// hand-written, deterministically-identified, fifty-row fixture whose every row
/// exists to be NAMED by an assertion, one parameterised INSERT at a time. That
/// is exactly right for proving correctness and exactly wrong for half a million
/// rows: it would take hours, and nothing here is ever asserted about
/// individually. Same schema knowledge, different mechanism - so the column lists
/// below mirror <c>LegacyData</c>'s and must be kept in step with it.
///
/// <b>The rows are genuinely valid, not filler.</b> Every booking session points
/// at a page that exists, every page at an organizer that exists, and
/// <see cref="SqlBulkCopyOptions.CheckConstraints"/> is set so SQL Server
/// verifies that rather than taking our word for it (SqlBulkCopy skips foreign
/// key checks by default). The filtered-unique BookingReference and PublicToken
/// indexes are real at this schema level, so both are generated unique per row -
/// a duplicate would fail the load rather than quietly inflate a count.
/// </summary>
public static class LegacyVolumeSeeder
{
    /// <summary>Rows per SqlBulkCopy batch. Big enough to amortise the round trip, small enough to keep the DataTable's memory bounded.</summary>
    private const int ChunkSize = 25_000;

    /// <summary>
    /// <paramref name="ProbeOrganizerId"/> and <paramref name="ProbeDate"/> are
    /// what <see cref="ConcurrentReader"/> asks about: a real organizer that owns
    /// real pages, and a date that genuinely has bookings on it. A probe that
    /// matched nothing would be answered from an index seek that touches one page
    /// and would prove nothing about whether the table was locked.
    /// </summary>
    public sealed record Result(
        TimeSpan Elapsed, int Organizers, int Pages, int Exceptions, int Sessions, long DataSizeMb,
        Guid ProbeOrganizerId, DateOnly ProbeDate);

    public static async Task<Result> SeedAsync(VolumeProfile profile, Action<string> log)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var connection = await VolumeDiagnosticDatabase.OpenAsync();

        var organizerIds = await SeedOrganizersAsync(connection, profile);
        log($"  seeded {organizerIds.Length:N0} organizers");

        var pageIds = await SeedPagesAsync(connection, profile, organizerIds);
        log($"  seeded {pageIds.Length:N0} booking pages");

        await SeedExceptionsAsync(connection, profile, organizerIds, log);
        log($"  seeded {profile.Exceptions:N0} availability exceptions");

        await SeedSessionsAsync(connection, profile, pageIds, log);
        log($"  seeded {profile.Sessions:N0} booking sessions");

        // Without this the optimizer costs every plan - the migrations' own
        // internal scans, and the concurrent reader's - from statistics gathered
        // when the tables were empty. The rows would be there and the seeks would
        // not, which is the same trap BookingVolume documents.
        await ExecuteAsync(connection, """
            UPDATE STATISTICS [BookingSessions] WITH FULLSCAN;
            UPDATE STATISTICS [BookingPages] WITH FULLSCAN;
            UPDATE STATISTICS [AvailabilityExceptions] WITH FULLSCAN;
            UPDATE STATISTICS [Organizers] WITH FULLSCAN;
            """, timeoutSeconds: 600);

        var sizeMb = await ScalarAsync<long>(connection, """
            SELECT ISNULL(SUM(a.total_pages), 0) * 8 / 1024
            FROM sys.allocation_units AS a
            JOIN sys.partitions AS p ON p.partition_id = a.container_id;
            """);

        stopwatch.Stop();
        return new Result(
            stopwatch.Elapsed, organizerIds.Length, pageIds.Length, profile.Exceptions, profile.Sessions, sizeMb,
            ProbeOrganizerId: organizerIds[0],
            ProbeDate: SessionFirstDate.AddDays(30));
    }

    /// <summary>
    /// The earliest date a seeded booking sits on. Six months back, so the
    /// seeded history spans a year either side of it - which is the shape real
    /// booking data has, and the shape that makes a date predicate selective.
    /// </summary>
    private static DateOnly SessionFirstDate => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-180);

    // ---- organizers ---------------------------------------------------------

    private static async Task<Guid[]> SeedOrganizersAsync(SqlConnection connection, VolumeProfile profile)
    {
        var ids = new Guid[profile.Organizers];
        var table = NewTable(
            ("Id", typeof(Guid)), ("Name", typeof(string)), ("Email", typeof(string)),
            ("PasswordHash", typeof(string)), ("CreatedAt", typeof(DateTime)));

        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Deterministic(0x01, i);
            table.Rows.Add(ids[i], $"Volume Organizer {i}", $"volume-organizer-{i}@volume.invalid",
                "not-a-real-hash", Created);
        }

        await BulkCopyAsync(connection, "Organizers", table);
        return ids;
    }

    // ---- booking pages ------------------------------------------------------

    private static async Task<Guid[]> SeedPagesAsync(SqlConnection connection, VolumeProfile profile, Guid[] organizerIds)
    {
        var ids = new Guid[profile.Pages];
        var table = NewTable(
            ("Id", typeof(Guid)), ("OrganizerId", typeof(Guid)), ("Slug", typeof(string)),
            ("Title", typeof(string)), ("Description", typeof(string)), ("DurationMinutes", typeof(int)),
            ("BufferBeforeMinutes", typeof(int)), ("BufferAfterMinutes", typeof(int)), ("IsActive", typeof(bool)),
            ("MinNoticeMinutes", typeof(int)), ("MaxBookingWindowDays", typeof(int)),
            ("MaxBookingsPerDay", typeof(int)), ("CreatedAt", typeof(DateTime)));

        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Deterministic(0x02, i);
            table.Rows.Add(
                ids[i], organizerIds[i / profile.PagesPerOrganizer], $"volume-page-{i}", $"Volume page {i}",
                i % 3 == 0 ? null : $"A booking page seeded for volume level measurement ({i}).",
                30, 0, 0, i % 7 != 0, DBNull.Value, DBNull.Value, DBNull.Value, Created);
        }

        await BulkCopyAsync(connection, "BookingPages", table);
        return ids;
    }

    // ---- availability exceptions (what the backfill UPDATE rewrites) ---------

    private static async Task SeedExceptionsAsync(
        SqlConnection connection, VolumeProfile profile, Guid[] organizerIds, Action<string> log)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var start = 0; start < profile.Exceptions; start += ChunkSize)
        {
            var table = NewTable(
                ("Id", typeof(Guid)), ("OrganizerId", typeof(Guid)), ("Date", typeof(DateTime)),
                ("StartTime", typeof(TimeSpan)), ("EndTime", typeof(TimeSpan)), ("Type", typeof(byte)),
                ("Reason", typeof(string)), ("CreatedAt", typeof(DateTime)));

            var end = Math.Min(start + ChunkSize, profile.Exceptions);
            for (var i = start; i < end; i++)
            {
                // A third carry a time window, so the backfill has rows whose
                // StartTime/EndTime it must leave alone as well as rows where
                // they are null. Spread across two years either side of today,
                // so the index the migration rebuilds has real key distribution
                // rather than one value.
                var timed = i % 3 == 0;
                table.Rows.Add(
                    Deterministic(0x03, i),
                    organizerIds[i % organizerIds.Length],
                    today.AddDays(-365 + i % 730).ToDateTime(TimeOnly.MinValue),
                    timed ? (object)new TimeOnly(10, 0).ToTimeSpan() : DBNull.Value,
                    timed ? (object)new TimeOnly(12, 0).ToTimeSpan() : DBNull.Value,
                    (byte)(i % 6),
                    i % 4 == 0 ? null : $"Volume exception {i}",
                    Created);
            }

            await BulkCopyAsync(connection, "AvailabilityExceptions", table);
            if (end < profile.Exceptions) log($"    ...{end:N0} exceptions");
        }
    }

    // ---- booking sessions (the volume driver) -------------------------------

    private static async Task SeedSessionsAsync(
        SqlConnection connection, VolumeProfile profile, Guid[] pageIds, Action<string> log)
    {
        var firstDate = SessionFirstDate;

        for (var start = 0; start < profile.Sessions; start += ChunkSize)
        {
            var table = NewTable(
                ("Id", typeof(Guid)), ("BookingPageId", typeof(Guid)), ("Status", typeof(byte)),
                ("Name", typeof(string)), ("Email", typeof(string)), ("Phone", typeof(string)),
                ("Message", typeof(string)), ("SelectedDate", typeof(DateTime)), ("SelectedTime", typeof(TimeSpan)),
                ("BookingReference", typeof(string)), ("PublicToken", typeof(string)),
                ("CancelledAt", typeof(DateTime)), ("CancelledBy", typeof(byte)), ("CancellationReason", typeof(string)),
                ("RescheduledAt", typeof(DateTime)), ("RescheduleCount", typeof(int)),
                ("AbandonedAt", typeof(DateTime)), ("SubmittedAt", typeof(DateTime)),
                ("CreatedAt", typeof(DateTime)), ("LastActivityAt", typeof(DateTime)),
                ("LastClientSequenceNumber", typeof(int)),
                ("LastClientIp", typeof(string)), ("LastUserAgent", typeof(string)));

            var end = Math.Min(start + ChunkSize, profile.Sessions);
            for (var i = start; i < end; i++)
            {
                // A realistic mix rather than all-Submitted: Status is the middle
                // key column of the index NarrowBookingConflictLockFootprint
                // builds, and an index leading on a column with one value in
                // practice is a different thing to measure.
                //   0 Active  1 Submitted  2 Abandoned  3 Cancelled
                var bucket = i % 20;
                var status = bucket switch
                {
                    < 12 => (byte)1, // Submitted   60%
                    < 15 => (byte)3, // Cancelled   15%
                    < 18 => (byte)2, // Abandoned   15%
                    _ => (byte)0,    // Active      10%
                };
                var booked = status is 1 or 3;

                // Every third row carries a real message. This is what
                // AddBookingSessionMessageMaxLength's ALTER COLUMN has to rewrite:
                // an all-null column would make that migration look cheaper than
                // it is. Comfortably inside the 2000 the narrowing imposes, and
                // inside the 8000-byte in-row limit, so the values are stored
                // where a real booking's message would be rather than as LOB
                // pointers.
                var message = i % 3 == 0
                    ? $"Volume booking {i}. " + new string('m', 200 + i % 600)
                    : null;

                table.Rows.Add(
                    Deterministic(0x04, i),
                    pageIds[i % pageIds.Length],
                    status,
                    $"Volume Guest {i}",
                    $"volume-guest-{i}@volume.invalid",
                    i % 5 == 0 ? $"+3897{i % 10_000_000:D7}" : null,
                    (object?)message ?? DBNull.Value,
                    booked ? (object)firstDate.AddDays(i % 365).ToDateTime(TimeOnly.MinValue) : DBNull.Value,
                    booked ? (object)new TimeOnly(9, 0).AddMinutes(15 * (i % 32)).ToTimeSpan() : DBNull.Value,
                    // Filtered-unique at this schema level, so unique or null.
                    booked ? (object)$"VOL{i:D8}" : DBNull.Value,
                    booked ? (object)$"volume-public-token-{i:D9}" : DBNull.Value,
                    status == 3 ? (object)Created.AddHours(3) : DBNull.Value,
                    status == 3 ? (object)(byte)0 : DBNull.Value,
                    status == 3 ? "Volume cancellation" : (object)DBNull.Value,
                    i % 11 == 0 && status == 1 ? (object)Created.AddHours(5) : DBNull.Value,
                    i % 11 == 0 && status == 1 ? 1 : 0,
                    status == 2 ? (object)Created.AddHours(1) : DBNull.Value,
                    booked ? (object)Created.AddHours(1) : DBNull.Value,
                    Created,
                    Created.AddMinutes(10),
                    booked ? 7 : 3,
                    // ClientContext is a REQUIRED owned type, so both are non-null
                    // on every row - the same call LegacyData makes.
                    "203.0.113.7",
                    "VolumeBrowser/1.0");
            }

            await BulkCopyAsync(connection, "BookingSessions", table);
            if (end < profile.Sessions) log($"    ...{end:N0} sessions");
        }
    }

    // ---- plumbing -----------------------------------------------------------

    private static readonly DateTime Created = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A stable Guid from a table discriminator and an ordinal. No randomness, so
    /// a rerun at the same volume produces the same keys - and no collisions
    /// between tables, since the discriminator is a distinct byte.
    /// </summary>
    private static Guid Deterministic(byte table, int ordinal)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, ordinal);
        bytes[15] = table;
        return new Guid(bytes);
    }

    private static DataTable NewTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type).AllowDBNull = true;
        }
        return table;
    }

    private static async Task BulkCopyAsync(SqlConnection connection, string destination, DataTable table)
    {
        // CheckConstraints because SqlBulkCopy skips foreign key checks by
        // default, and rows that only look valid would make every measurement
        // below a measurement of something the application could never produce.
        // TableLock because this is the SEED, not the thing being measured - no
        // concurrent reader exists yet at this point.
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = destination,
            BatchSize = 5_000,
            BulkCopyTimeout = 600,
        };

        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, int timeoutSeconds = 120)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T))!;
    }
}
