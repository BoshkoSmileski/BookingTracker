using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// What the database held immediately BEFORE the first Down migration ran.
///
/// The assertions after each rollback step compare against this rather than
/// against the seeder's constants, for the reason <c>LegacySnapshot</c> gives
/// about the upgrade path: comparing to the seeder would only prove the rollback
/// agrees with what the test MEANT to write, whereas comparing to a snapshot
/// makes it "what the database held" against "what the database holds", with the
/// Down migration the only thing in between.
///
/// <b>Read with raw SQL, and that is forced.</b> After the first rollback step the
/// current <c>DbContext</c> can no longer read most of these tables - its model
/// names columns that no longer exist - so a snapshot taken through EF could not
/// be re-read afterwards to compare against. Everything here is therefore column
/// lists this file names explicitly, which also means a rollback that quietly
/// changed a column's contents would be visible rather than hidden behind a
/// projection.
/// </summary>
public sealed record PreRollbackSnapshot(
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyList<string> Tables,
    IReadOnlyList<Guid> OrganizerIds,
    IReadOnlyList<Guid> BookingPageIds,
    IReadOnlyList<Guid> BookingSessionIds,
    IReadOnlyList<SnapshotEvent> Events,
    IReadOnlyList<SnapshotSession> Sessions,
    IReadOnlyList<SnapshotException> Exceptions,
    IReadOnlyList<SnapshotOrganizer> Organizers,
    IReadOnlyList<SnapshotPage> Pages,
    IReadOnlyList<SnapshotInterval> WorkingIntervals,
    IReadOnlyList<SnapshotAnswer> Answers)
{
    /// <summary>
    /// Every table the rollback path touches, plus the four it must not.
    /// Counted before, so "nothing else changed" is checkable rather than assumed.
    /// </summary>
    public static readonly IReadOnlyList<string> CountedTables =
    [
        "Organizers", "BookingPages", "BookingSessions", "BookingSessionEvents",
        "RefreshTokens", "WorkingSchedules", "WorkingDays", "WorkingDayIntervals",
        "AvailabilityExceptions", "AvailabilityOverrides", "AvailabilityOverrideRanges",
        "BookingQuestions", "BookingFormFields", "BookingSessionAnswers",
        "CalendarConnections", "CalendarSyncedEvents",
        "NotificationSettings", "EmailNotifications", "BookingReminders",
    ];

    public static async Task<PreRollbackSnapshot> CaptureAsync()
    {
        var schema = DownTestDatabase.Schema;

        var counts = new Dictionary<string, int>();
        foreach (var table in CountedTables) counts[table] = await schema.RowCountAsync(table);

        return new PreRollbackSnapshot(
            counts,
            await schema.TablesAsync(),
            await IdsAsync("SELECT Id FROM Organizers ORDER BY Id;"),
            await IdsAsync("SELECT Id FROM BookingPages ORDER BY Id;"),
            await IdsAsync("SELECT Id FROM BookingSessions ORDER BY Id;"),
            await EventsAsync(),
            await SessionsAsync(),
            await ExceptionsAsync(),
            await OrganizersAsync(),
            await PagesAsync(),
            await IntervalsAsync(),
            await AnswersAsync());
    }

    // ---- readers -------------------------------------------------------------

    private static async Task<IReadOnlyList<Guid>> IdsAsync(string sql)
    {
        var ids = new List<Guid>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    /// <summary>
    /// The event log, in the order <c>Rebuild()</c> replays it. No Down migration
    /// in this path touches BookingSessionEvents, so this list is the strongest
    /// "nothing was lost" claim the suite can make - and the reason several
    /// destructive rollbacks turn out to be recoverable.
    /// </summary>
    private static async Task<IReadOnlyList<SnapshotEvent>> EventsAsync()
    {
        var events = new List<SnapshotEvent>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT SessionId, EventType, ISNULL(FieldName, ''), ISNULL(OldValue, ''), ISNULL(NewValue, ''), ClientSequenceNumber
            FROM BookingSessionEvents
            ORDER BY SessionId, ClientSequenceNumber, EventType;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new SnapshotEvent(
                reader.GetGuid(0), reader.GetByte(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
        }
        return events;
    }

    private static async Task<IReadOnlyList<SnapshotSession>> SessionsAsync()
    {
        var sessions = new List<SnapshotSession>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id, BookingPageId, Status, ISNULL(Name, ''), ISNULL(Email, ''), ISNULL(Message, ''),
                   SelectedDate, SelectedTime, ISNULL(BookingReference, ''), ISNULL(PublicToken, ''),
                   ISNULL(MeetingUrl, ''), RescheduleCount, ISNULL(LastClientIp, ''), LastClientSequenceNumber
            FROM BookingSessions ORDER BY Id;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new SnapshotSession(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetByte(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.IsDBNull(7) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(7)),
                reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetInt32(11), reader.GetString(12), reader.GetInt32(13)));
        }
        return sessions;
    }

    private static async Task<IReadOnlyList<SnapshotException>> ExceptionsAsync()
    {
        var exceptions = new List<SnapshotException>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id, OrganizerId, [Date], EndDate, StartTime, EndTime, [Type], ISNULL(Reason, '')
            FROM AvailabilityExceptions ORDER BY Id;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            exceptions.Add(new SnapshotException(
                reader.GetGuid(0), reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(4)),
                reader.IsDBNull(5) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(5)),
                reader.GetByte(6), reader.GetString(7)));
        }
        return exceptions;
    }

    private static async Task<IReadOnlyList<SnapshotOrganizer>> OrganizersAsync()
    {
        var organizers = new List<SnapshotOrganizer>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Id, Name, Email, ISNULL(PasswordHash, '') FROM Organizers ORDER BY Id;", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            organizers.Add(new SnapshotOrganizer(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return organizers;
    }

    private static async Task<IReadOnlyList<SnapshotPage>> PagesAsync()
    {
        var pages = new List<SnapshotPage>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id, OrganizerId, Slug, Title, ISNULL(Description, ''), DurationMinutes,
                   BufferBeforeMinutes, BufferAfterMinutes, IsActive, MeetingProvider
            FROM BookingPages ORDER BY Id;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            pages.Add(new SnapshotPage(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetBoolean(8), reader.GetByte(9)));
        }
        return pages;
    }

    /// <summary>An owned collection, keyed (WorkingDayId, Id) with an IDENTITY surrogate.</summary>
    private static async Task<IReadOnlyList<SnapshotInterval>> IntervalsAsync()
    {
        var intervals = new List<SnapshotInterval>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT WorkingDayId, StartTime, EndTime FROM WorkingDayIntervals ORDER BY WorkingDayId, StartTime;", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            intervals.Add(new SnapshotInterval(
                reader.GetGuid(0), TimeOnly.FromTimeSpan(reader.GetTimeSpan(1)), TimeOnly.FromTimeSpan(reader.GetTimeSpan(2))));
        }
        return intervals;
    }

    private static async Task<IReadOnlyList<SnapshotAnswer>> AnswersAsync()
    {
        var answers = new List<SnapshotAnswer>();
        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT BookingSessionId, BookingFormFieldId, Value FROM BookingSessionAnswers ORDER BY BookingSessionId, BookingFormFieldId;",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            answers.Add(new SnapshotAnswer(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }
        return answers;
    }
}

public sealed record SnapshotEvent(
    Guid SessionId, byte EventType, string FieldName, string OldValue, string NewValue, int Sequence);

public sealed record SnapshotSession(
    Guid Id, Guid BookingPageId, byte Status, string Name, string Email, string Message,
    DateOnly? SelectedDate, TimeOnly? SelectedTime, string BookingReference, string PublicToken,
    string MeetingUrl, int RescheduleCount, string LastClientIp, int LastClientSequenceNumber);

public sealed record SnapshotException(
    Guid Id, Guid OrganizerId, DateOnly Date, DateOnly EndDate,
    TimeOnly? StartTime, TimeOnly? EndTime, byte Type, string Reason);

public sealed record SnapshotOrganizer(Guid Id, string Name, string Email, string PasswordHash);

public sealed record SnapshotPage(
    Guid Id, Guid OrganizerId, string Slug, string Title, string Description,
    int DurationMinutes, int BufferBefore, int BufferAfter, bool IsActive, byte MeetingProvider);

public sealed record SnapshotInterval(Guid WorkingDayId, TimeOnly Start, TimeOnly End);

public sealed record SnapshotAnswer(Guid BookingSessionId, Guid BookingFormFieldId, string Value);
