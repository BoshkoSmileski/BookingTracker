using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// What the legacy database actually held, read back out of it immediately
/// BEFORE the migration runs.
///
/// The assertions afterwards compare against this rather than against
/// <see cref="LegacyData"/>'s constants, and the difference matters: comparing
/// to the seeder would only prove the migration agrees with what the test MEANT
/// to write. Reading the rows back first means the comparison is
/// "what the database held" against "what the database holds", with the
/// migration the only thing that happened in between.
///
/// Read with raw SQL for the same reason the seed is written with it - there is
/// no <c>DbContext</c> that matches the legacy schema.
/// </summary>
public sealed class LegacySnapshot
{
    /// <summary>Row count per table, before migrating.</summary>
    public required IReadOnlyDictionary<string, int> RowCounts { get; init; }

    /// <summary>Every AvailabilityException, keyed by id - the rows the backfill rewrites.</summary>
    public required IReadOnlyDictionary<Guid, LegacyException> Exceptions { get; init; }

    /// <summary>Every BookingSession, keyed by id.</summary>
    public required IReadOnlyDictionary<Guid, LegacySessionRow> Sessions { get; init; }

    /// <summary>Every id in the tables whose primary key is a Guid the application chose.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<Guid>> Ids { get; init; }

    /// <summary>The event log, in ClientSequenceNumber order, as (sequence, type, field, value).</summary>
    public required IReadOnlyList<(int Sequence, byte EventType, string? FieldName, string? NewValue)> Events { get; init; }

    public sealed record LegacyException(
        Guid Id, Guid OrganizerId, DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, byte Type, string? Reason);

    public sealed record LegacySessionRow(
        Guid Id, Guid BookingPageId, byte Status, string? Name, string? Email, string? Message,
        DateOnly? SelectedDate, TimeOnly? SelectedTime, string? BookingReference, string? PublicToken,
        int RescheduleCount, DateTime? SubmittedAt, DateTime? CancelledAt, byte? CancelledBy);

    /// <summary>Tables whose rows this suite asserts survive, in dependency order.</summary>
    public static readonly IReadOnlyList<string> Tables =
    [
        "Organizers", "BookingPages", "BookingQuestions",
        "WorkingSchedules", "WorkingDays", "WorkingDayIntervals",
        "AvailabilityExceptions", "BookingSessions", "BookingSessionEvents",
        "RefreshTokens", "CalendarConnections", "CalendarSyncedEvents",
    ];

    /// <summary>Tables with an application-assigned Guid primary key, so the ids are comparable across the migration.</summary>
    private static readonly IReadOnlyList<string> GuidKeyedTables =
    [
        "Organizers", "BookingPages", "BookingQuestions",
        "WorkingSchedules", "WorkingDays",
        "AvailabilityExceptions", "BookingSessions",
        "RefreshTokens", "CalendarConnections", "CalendarSyncedEvents",
    ];

    public static async Task<LegacySnapshot> CaptureAsync()
    {
        await using var connection = await MigrationTestDatabase.OpenAsync();

        var counts = new Dictionary<string, int>();
        foreach (var table in Tables)
        {
            counts[table] = await ScalarIntAsync(connection, $"SELECT COUNT(*) FROM [{table}];");
        }

        var ids = new Dictionary<string, IReadOnlyList<Guid>>();
        foreach (var table in GuidKeyedTables)
        {
            ids[table] = await GuidsAsync(connection, $"SELECT Id FROM [{table}] ORDER BY Id;");
        }

        return new LegacySnapshot
        {
            RowCounts = counts,
            Ids = ids,
            Exceptions = await ReadExceptionsAsync(connection),
            Sessions = await ReadSessionsAsync(connection),
            Events = await ReadEventsAsync(connection),
        };
    }

    private static async Task<IReadOnlyDictionary<Guid, LegacyException>> ReadExceptionsAsync(SqlConnection connection)
    {
        // Deliberately does NOT select EndDate: the legacy schema has no such
        // column, and naming it here would make this file fail to run against
        // exactly the database it exists to read.
        const string sql = "SELECT Id, OrganizerId, Date, StartTime, EndTime, Type, Reason FROM AvailabilityExceptions;";

        var rows = new Dictionary<Guid, LegacyException>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            rows[id] = new LegacyException(
                id,
                reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                ReadTime(reader, 3),
                ReadTime(reader, 4),
                reader.GetByte(5),
                reader.IsDBNull(6) ? null : reader.GetString(6));
        }
        return rows;
    }

    private static async Task<IReadOnlyDictionary<Guid, LegacySessionRow>> ReadSessionsAsync(SqlConnection connection)
    {
        const string sql = """
            SELECT Id, BookingPageId, Status, Name, Email, Message,
                   SelectedDate, SelectedTime, BookingReference, PublicToken,
                   RescheduleCount, SubmittedAt, CancelledAt, CancelledBy
            FROM BookingSessions;
            """;

        var rows = new Dictionary<Guid, LegacySessionRow>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            rows[id] = new LegacySessionRow(
                id,
                reader.GetGuid(1),
                reader.GetByte(2),
                ReadString(reader, 3),
                ReadString(reader, 4),
                ReadString(reader, 5),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                ReadTime(reader, 7),
                ReadString(reader, 8),
                ReadString(reader, 9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                reader.IsDBNull(13) ? null : reader.GetByte(13));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<(int, byte, string?, string?)>> ReadEventsAsync(SqlConnection connection)
    {
        const string sql = """
            SELECT ClientSequenceNumber, EventType, FieldName, NewValue
            FROM BookingSessionEvents
            ORDER BY ClientSequenceNumber;
            """;

        var rows = new List<(int, byte, string?, string?)>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetByte(1), ReadString(reader, 2), ReadString(reader, 3)));
        }
        return rows;
    }

    private static string? ReadString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>
    /// Read as TimeSpan rather than TimeOnly for the same version-independence
    /// reason the seed converts on the way in.
    /// </summary>
    private static TimeOnly? ReadTime(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(ordinal));

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<Guid>> GuidsAsync(SqlConnection connection, string sql)
    {
        var ids = new List<Guid>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }
}
