using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// The rows a database created by the OLD version of BookingTracker would hold,
/// written straight into the legacy schema.
///
/// **Raw SQL rather than EF, and that is forced rather than chosen.**
/// <c>BookingTrackerDbContext</c> is the CURRENT model: asking it to insert an
/// AvailabilityException would emit an INSERT naming <c>EndDate</c>, a column
/// the legacy schema does not have. There is no version of the context that
/// matches the baseline, and reconstructing one would be a second model to keep
/// in step with the migrations - which is the drift this suite exists to catch.
/// Parameterised <see cref="SqlCommand"/> against the columns the baseline
/// designer snapshot declares is the honest way to produce "data an older
/// version wrote".
///
/// **Deterministic ids.** Every row has a hardcoded Guid, so a post-migration
/// assertion can name the row it is about instead of finding it by ordering.
/// Dates are the one thing that cannot be hardcoded: slot generation reads
/// <c>DateTime.UtcNow</c>, so a fixed calendar date silently changes meaning as
/// the wall clock rolls forward. They are computed
/// relative to now and captured in the snapshot instead.
///
/// **Representative, not exhaustive.** Every row here earns its place by
/// exercising something a migration in the tested range actually does: the four
/// availability exceptions are what the backfill rewrites, the 2000-character
/// message is the boundary of the column the narrowing migration imposes, the
/// booking pages are what gains a non-nullable MeetingProvider, and the
/// submitted sessions are the rows the rebuilt conflict index has to keep
/// finding.
/// </summary>
public static class LegacyData
{
    // ---- Organizer A: the fully-configured one -------------------------------

    public static readonly Guid AdaId = new("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid AdaScheduleId = new("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid AdaConsultationPageId = new("a0000000-0000-0000-0000-000000000010");
    public static readonly Guid AdaWorkshopPageId = new("a0000000-0000-0000-0000-000000000011");
    public static readonly Guid AdaQuestionOneId = new("a0000000-0000-0000-0000-000000000020");
    public static readonly Guid AdaQuestionTwoId = new("a0000000-0000-0000-0000-000000000021");
    public static readonly Guid AdaCalendarConnectionId = new("a0000000-0000-0000-0000-000000000030");
    public static readonly Guid AdaSyncedEventId = new("a0000000-0000-0000-0000-000000000031");

    public const string AdaEmail = "ada@legacy.invalid";
    public const string AdaConsultationSlug = "legacy-consultation";
    public const string AdaWorkshopSlug = "legacy-workshop";
    public const string AdaTimeZoneId = "Europe/Skopje";

    /// <summary>Whole-day, no times. The plain case the backfill must repair.</summary>
    public static readonly Guid WholeDayExceptionId = new("a0000000-0000-0000-0000-000000000040");

    /// <summary>A 10:00-12:00 window. Proves the backfill leaves the times alone.</summary>
    public static readonly Guid TimedExceptionId = new("a0000000-0000-0000-0000-000000000041");

    /// <summary>Already in the past when the migration runs. Must still be repaired, not skipped.</summary>
    public static readonly Guid PastExceptionId = new("a0000000-0000-0000-0000-000000000042");

    /// <summary>Far enough ahead that no clock skew can make it "today".</summary>
    public static readonly Guid FarFutureExceptionId = new("a0000000-0000-0000-0000-000000000043");

    public static readonly Guid SubmittedSessionId = new("a0000000-0000-0000-0000-000000000050");
    public static readonly Guid ActiveSessionId = new("a0000000-0000-0000-0000-000000000051");
    public static readonly Guid CancelledSessionId = new("a0000000-0000-0000-0000-000000000052");
    public static readonly Guid AbandonedSessionId = new("a0000000-0000-0000-0000-000000000053");
    public static readonly Guid RescheduledSessionId = new("a0000000-0000-0000-0000-000000000054");

    public static readonly Guid ActiveRefreshTokenId = new("a0000000-0000-0000-0000-000000000060");
    public static readonly Guid RevokedRefreshTokenId = new("a0000000-0000-0000-0000-000000000061");

    public const string SubmittedBookingReference = "LEGACY-A1";
    public const string SubmittedPublicToken = "legacy-public-token-submitted-0000000001";
    public const string RescheduledBookingReference = "LEGACY-A2";
    public const string RescheduledPublicToken = "legacy-public-token-rescheduled-000000001";
    public const string CancelledBookingReference = "LEGACY-A3";
    public const string CancelledPublicToken = "legacy-public-token-cancelled-00000000001";

    /// <summary>
    /// Exactly <c>BookingFieldLimits.MessageMaxLength</c> characters - the widest
    /// value the narrowed nvarchar(2000) column can still hold. Legacy rows lived
    /// in nvarchar(max) with no validator in front of it, so the boundary is the
    /// case worth pinning: one character more and SQL Server would refuse the
    /// ALTER COLUMN outright.
    /// </summary>
    public static readonly string BoundaryMessage = new('m', 2000);

    // ---- Organizer B: a second, untouched tenant -----------------------------

    public static readonly Guid BenId = new("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid BenScheduleId = new("b0000000-0000-0000-0000-000000000002");
    public static readonly Guid BenPageId = new("b0000000-0000-0000-0000-000000000010");
    public static readonly Guid BenSessionId = new("b0000000-0000-0000-0000-000000000050");
    public static readonly Guid BenExceptionId = new("b0000000-0000-0000-0000-000000000040");
    public const string BenEmail = "ben@legacy.invalid";
    public const string BenSlug = "legacy-ben-intro";

    // ---- Organizer C: registered, never configured anything ------------------
    //
    // A real legacy database has these. It is also the shape that proves the
    // migration does not require a schedule, a page or a session to exist.

    public static readonly Guid CaraId = new("c0000000-0000-0000-0000-000000000001");
    public const string CaraEmail = "cara@legacy.invalid";

    /// <summary>Row counts the migration must not change, by table.</summary>
    public static readonly IReadOnlyDictionary<string, int> ExpectedRowCounts = new Dictionary<string, int>
    {
        ["Organizers"] = 3,
        ["BookingPages"] = 3,
        ["BookingQuestions"] = 2,
        ["WorkingSchedules"] = 2,
        ["WorkingDays"] = 14,
        ["WorkingDayIntervals"] = 11,
        ["AvailabilityExceptions"] = 5,
        ["BookingSessions"] = 6,
        ["BookingSessionEvents"] = 8,
        ["RefreshTokens"] = 2,
        ["CalendarConnections"] = 1,
        ["CalendarSyncedEvents"] = 1,
    };

    /// <summary>
    /// The calendar dates the seed used, resolved once at seed time so every
    /// assertion in the suite talks about the same days the rows were written
    /// with.
    /// </summary>
    public sealed record Dates(
        DateOnly BookedDate,
        DateOnly RescheduledDate,
        DateOnly WholeDayBlocked,
        DateOnly TimedBlocked,
        DateOnly PastBlocked,
        DateOnly FarFutureBlocked,
        DateOnly BenBlocked,
        DateOnly PostMigrationBooking,
        DateOnly UnblockedControl);

    public static Dates ResolveDates()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Each is its own weekday, far enough out to clear the same-day cutoff
        // and any minimum notice, and far enough apart that none can collide
        // with another as the wall clock rolls forward.
        return new Dates(
            BookedDate: NextWeekday(today.AddDays(14)),
            RescheduledDate: NextWeekday(today.AddDays(17)),
            WholeDayBlocked: NextWeekday(today.AddDays(21)),
            TimedBlocked: NextWeekday(today.AddDays(24)),
            PastBlocked: today.AddDays(-30),
            FarFutureBlocked: NextWeekday(today.AddDays(200)),
            BenBlocked: NextWeekday(today.AddDays(28)),

            // Not seeded with anything. The post-migration write needs a day no
            // other assertion in the suite reads, so a booking made by one test
            // cannot change what another test is measuring.
            PostMigrationBooking: NextWeekday(today.AddDays(35)),

            // Also unseeded, and the control for the blocked-date assertions: a
            // day with no exception on it must still offer slots, otherwise
            // "the blocked day offers none" would be true for the wrong reason.
            UnblockedControl: NextWeekday(today.AddDays(38)));

        static DateOnly NextWeekday(DateOnly from)
        {
            while (from.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) from = from.AddDays(1);
            return from;
        }
    }

    public static async Task SeedAsync(Dates dates)
    {
        await using var connection = await MigrationTestDatabase.OpenAsync();

        var created = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        // ---- organizers ------------------------------------------------------
        await ExecAsync(connection,
            "INSERT INTO Organizers (Id, Name, Email, PasswordHash, CreatedAt) VALUES (@id, @name, @email, @hash, @created);",
            ("@id", AdaId), ("@name", "Ada Legacy"), ("@email", AdaEmail), ("@hash", "legacy-hash-ada"), ("@created", created));

        await ExecAsync(connection,
            "INSERT INTO Organizers (Id, Name, Email, PasswordHash, CreatedAt) VALUES (@id, @name, @email, @hash, @created);",
            ("@id", BenId), ("@name", "Ben Legacy"), ("@email", BenEmail), ("@hash", "legacy-hash-ben"), ("@created", created));

        await ExecAsync(connection,
            "INSERT INTO Organizers (Id, Name, Email, PasswordHash, CreatedAt) VALUES (@id, @name, @email, @hash, @created);",
            ("@id", CaraId), ("@name", "Cara Legacy"), ("@email", CaraEmail), ("@hash", "legacy-hash-cara"), ("@created", created));

        // ---- booking pages ---------------------------------------------------
        await AddPageAsync(connection, AdaConsultationPageId, AdaId, AdaConsultationSlug,
            "Legacy consultation", "Booked under the old schema.", durationMinutes: 30, isActive: true, created);
        await AddPageAsync(connection, AdaWorkshopPageId, AdaId, AdaWorkshopSlug,
            "Legacy workshop", description: null, durationMinutes: 60, isActive: false, created);
        await AddPageAsync(connection, BenPageId, BenId, BenSlug,
            "Legacy intro", description: null, durationMinutes: 15, isActive: true, created);

        await ExecAsync(connection,
            "INSERT INTO BookingQuestions (Id, BookingPageId, Prompt, DisplayOrder) VALUES (@id, @page, @prompt, @order);",
            ("@id", AdaQuestionOneId), ("@page", AdaConsultationPageId), ("@prompt", "Please have your account number ready."), ("@order", 0));
        await ExecAsync(connection,
            "INSERT INTO BookingQuestions (Id, BookingPageId, Prompt, DisplayOrder) VALUES (@id, @page, @prompt, @order);",
            ("@id", AdaQuestionTwoId), ("@page", AdaConsultationPageId), ("@prompt", "Arrive five minutes early."), ("@order", 1));

        // ---- working schedules ----------------------------------------------
        // Ada: Mon-Fri 09:00-17:00, with Wednesday split around a lunch break so
        // the owned-collection table holds more than one interval for one day.
        await AddScheduleAsync(connection, AdaScheduleId, AdaId, AdaTimeZoneId, created,
            wednesdaySplit: true);

        // Ben: Mon-Fri 09:00-17:00 in a different zone, single interval each.
        await AddScheduleAsync(connection, BenScheduleId, BenId, "UTC", created,
            wednesdaySplit: false);

        // Cara deliberately gets no schedule at all.

        // ---- availability exceptions (what the backfill rewrites) -------------
        await AddExceptionAsync(connection, WholeDayExceptionId, AdaId, dates.WholeDayBlocked,
            start: null, end: null, type: 1 /* Vacation */, reason: "Legacy whole-day block", created);
        await AddExceptionAsync(connection, TimedExceptionId, AdaId, dates.TimedBlocked,
            start: new TimeOnly(10, 0), end: new TimeOnly(12, 0), type: 2 /* Meeting */, reason: "Legacy standup", created);
        await AddExceptionAsync(connection, PastExceptionId, AdaId, dates.PastBlocked,
            start: null, end: null, type: 0 /* Holiday */, reason: null, created);
        await AddExceptionAsync(connection, FarFutureExceptionId, AdaId, dates.FarFutureBlocked,
            start: null, end: null, type: 5 /* Other */, reason: "Legacy far-future block", created);
        await AddExceptionAsync(connection, BenExceptionId, BenId, dates.BenBlocked,
            start: null, end: null, type: 3 /* SickLeave */, reason: null, created);

        // ---- booking sessions -------------------------------------------------
        await AddSessionAsync(connection, new LegacySession(
            Id: SubmittedSessionId, PageId: AdaConsultationPageId, Status: 1 /* Submitted */,
            Name: "Grace Guest", Email: "grace@legacy.invalid", Phone: "+38970000001",
            Message: BoundaryMessage,
            SelectedDate: dates.BookedDate, SelectedTime: new TimeOnly(10, 0),
            BookingReference: SubmittedBookingReference, PublicToken: SubmittedPublicToken,
            SubmittedAt: created.AddHours(1), LastClientSequenceNumber: 7));

        await AddSessionAsync(connection, new LegacySession(
            Id: ActiveSessionId, PageId: AdaConsultationPageId, Status: 0 /* Active */,
            Name: "Half Filled", Email: null, Phone: null, Message: null,
            SelectedDate: null, SelectedTime: null,
            BookingReference: null, PublicToken: null, SubmittedAt: null, LastClientSequenceNumber: 2));

        await AddSessionAsync(connection, new LegacySession(
            Id: CancelledSessionId, PageId: AdaConsultationPageId, Status: 3 /* Cancelled */,
            Name: "Carla Cancelled", Email: "carla@legacy.invalid", Phone: null,
            Message: "Changed my mind.",
            SelectedDate: dates.BookedDate, SelectedTime: new TimeOnly(14, 0),
            BookingReference: CancelledBookingReference, PublicToken: CancelledPublicToken,
            SubmittedAt: created.AddHours(2), LastClientSequenceNumber: 9,
            CancelledAt: created.AddHours(3), CancelledBy: 0 /* Customer */,
            CancellationReason: "Something came up"));

        await AddSessionAsync(connection, new LegacySession(
            Id: AbandonedSessionId, PageId: AdaWorkshopPageId, Status: 2 /* Abandoned */,
            Name: "Ann Abandoned", Email: "ann@legacy.invalid", Phone: null, Message: null,
            SelectedDate: null, SelectedTime: null,
            BookingReference: null, PublicToken: null, SubmittedAt: null, LastClientSequenceNumber: 4,
            AbandonedAt: created.AddHours(1)));

        await AddSessionAsync(connection, new LegacySession(
            Id: RescheduledSessionId, PageId: AdaConsultationPageId, Status: 1 /* Submitted */,
            Name: "Rob Rescheduled", Email: "rob@legacy.invalid", Phone: null, Message: null,
            SelectedDate: dates.RescheduledDate, SelectedTime: new TimeOnly(11, 0),
            BookingReference: RescheduledBookingReference, PublicToken: RescheduledPublicToken,
            SubmittedAt: created.AddHours(1), LastClientSequenceNumber: 20,
            RescheduledAt: created.AddHours(5), RescheduleCount: 2));

        await AddSessionAsync(connection, new LegacySession(
            Id: BenSessionId, PageId: BenPageId, Status: 0 /* Active */,
            Name: "Ben's visitor", Email: null, Phone: null, Message: null,
            SelectedDate: null, SelectedTime: null,
            BookingReference: null, PublicToken: null, SubmittedAt: null, LastClientSequenceNumber: 1));

        // ---- the event log behind the submitted booking ----------------------
        // Ordered by ClientSequenceNumber, which is what Rebuild() sorts on -
        // so a migration that reordered or dropped rows here would be visible.
        var sequence = 0;
        foreach (var (type, field, value) in new (int, string?, string?)[]
        {
            (0 /* SessionStarted */, null, null),
            (1 /* FieldChanged */, "Name", "Grace Guest"),
            (1 /* FieldChanged */, "Email", "grace@legacy.invalid"),
            (1 /* FieldChanged */, "Phone", "+38970000001"),
            (2 /* DateSelected */, null, dates.BookedDate.ToString("yyyy-MM-dd")),
            (3 /* TimeSelected */, null, "10:00"),
            (7 /* BookingSubmitted */, null, SubmittedBookingReference),
            (12 /* EmailSent */, "BookingConfirmation", "grace@legacy.invalid"),
        })
        {
            await ExecAsync(connection,
                """
                INSERT INTO BookingSessionEvents
                    (SessionId, BookingPageId, EventType, FieldName, OldValue, NewValue,
                     ClientSequenceNumber, Timestamp, ClientIp, UserAgent)
                VALUES (@session, @page, @type, @field, NULL, @value, @seq, @stamp, @ip, @ua);
                """,
                ("@session", SubmittedSessionId), ("@page", AdaConsultationPageId),
                ("@type", (byte)type), ("@field", field), ("@value", value),
                ("@seq", sequence), ("@stamp", created.AddMinutes(sequence)),
                ("@ip", "203.0.113.7"), ("@ua", "LegacyBrowser/1.0"));
            sequence++;
        }

        // ---- refresh tokens (one live, one already rotated) -------------------
        await ExecAsync(connection,
            """
            INSERT INTO RefreshTokens (Id, OrganizerId, Token, ExpiresAt, RevokedAt, ReplacedByToken, CreatedAt)
            VALUES (@id, @org, @token, @expires, NULL, NULL, @created);
            """,
            ("@id", ActiveRefreshTokenId), ("@org", AdaId), ("@token", "legacy-refresh-active"),
            ("@expires", created.AddDays(30)), ("@created", created));

        await ExecAsync(connection,
            """
            INSERT INTO RefreshTokens (Id, OrganizerId, Token, ExpiresAt, RevokedAt, ReplacedByToken, CreatedAt)
            VALUES (@id, @org, @token, @expires, @revoked, @replaced, @created);
            """,
            ("@id", RevokedRefreshTokenId), ("@org", AdaId), ("@token", "legacy-refresh-revoked"),
            ("@expires", created.AddDays(30)), ("@revoked", created.AddHours(1)),
            ("@replaced", "legacy-refresh-active"), ("@created", created));

        // ---- calendar connection + one synced event --------------------------
        await ExecAsync(connection,
            """
            INSERT INTO CalendarConnections
                (Id, OrganizerId, Provider, ExternalAccountEmail, ExternalCalendarId, ExternalCalendarName,
                 EncryptedAccessToken, EncryptedRefreshToken, AccessTokenExpiresAtUtc, Status,
                 ImportBusyEvents, ExportBookings, AutoDeleteCancelledBookings, AutoUpdateRescheduledBookings,
                 DefaultReminderMinutes, EventTitleFormat, EventVisibility,
                 LastSyncedAtUtc, LastFailedSyncAtUtc, LastSyncError, CreatedAt, UpdatedAt)
            VALUES (@id, @org, 0, @account, @calId, @calName,
                    @access, @refresh, @expires, 0,
                    1, 1, 1, 1,
                    30, @title, 'default',
                    @synced, NULL, NULL, @created, NULL);
            """,
            ("@id", AdaCalendarConnectionId), ("@org", AdaId),
            ("@account", "ada@legacy.invalid"), ("@calId", "legacy-calendar-id"), ("@calName", "Legacy calendar"),
            ("@access", "legacy-ciphertext-access"), ("@refresh", "legacy-ciphertext-refresh"),
            ("@expires", created.AddHours(1)), ("@title", "{Service Name} with {Guest Name}"),
            ("@synced", created.AddMinutes(30)), ("@created", created));

        await ExecAsync(connection,
            """
            INSERT INTO CalendarSyncedEvents (Id, BookingSessionId, CalendarConnectionId, ExternalEventId, CreatedAt, UpdatedAt)
            VALUES (@id, @session, @connection, @external, @created, NULL);
            """,
            ("@id", AdaSyncedEventId), ("@session", SubmittedSessionId),
            ("@connection", AdaCalendarConnectionId), ("@external", "legacy-google-event-id"), ("@created", created));
    }

    // ---- row builders --------------------------------------------------------

    private sealed record LegacySession(
        Guid Id, Guid PageId, byte Status, string? Name, string? Email, string? Phone, string? Message,
        DateOnly? SelectedDate, TimeOnly? SelectedTime, string? BookingReference, string? PublicToken,
        DateTime? SubmittedAt, int LastClientSequenceNumber,
        DateTime? CancelledAt = null, byte? CancelledBy = null, string? CancellationReason = null,
        DateTime? RescheduledAt = null, int RescheduleCount = 0, DateTime? AbandonedAt = null);

    private static Task AddSessionAsync(SqlConnection connection, LegacySession s)
    {
        var created = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        return ExecAsync(connection,
            """
            INSERT INTO BookingSessions
                (Id, BookingPageId, Status, Name, Email, Phone, Message,
                 SelectedDate, SelectedTime, BookingReference, PublicToken,
                 CancelledAt, CancelledBy, CancellationReason, RescheduledAt, RescheduleCount,
                 AbandonedAt, SubmittedAt, CreatedAt, LastActivityAt, LastClientSequenceNumber,
                 LastClientIp, LastUserAgent)
            VALUES (@id, @page, @status, @name, @email, @phone, @message,
                    @date, @time, @reference, @token,
                    @cancelledAt, @cancelledBy, @cancelReason, @rescheduledAt, @rescheduleCount,
                    @abandonedAt, @submittedAt, @created, @lastActivity, @lastSequence,
                    @ip, @ua);
            """,
            ("@id", s.Id), ("@page", s.PageId), ("@status", s.Status),
            ("@name", s.Name), ("@email", s.Email), ("@phone", s.Phone), ("@message", s.Message),
            ("@date", s.SelectedDate), ("@time", s.SelectedTime),
            ("@reference", s.BookingReference), ("@token", s.PublicToken),
            ("@cancelledAt", s.CancelledAt), ("@cancelledBy", s.CancelledBy), ("@cancelReason", s.CancellationReason),
            ("@rescheduledAt", s.RescheduledAt), ("@rescheduleCount", s.RescheduleCount),
            ("@abandonedAt", s.AbandonedAt), ("@submittedAt", s.SubmittedAt),
            ("@created", created), ("@lastActivity", created.AddMinutes(10)),
            ("@lastSequence", s.LastClientSequenceNumber),
            // Non-null on every row: ClientContext is a REQUIRED owned type, and
            // an all-null owned instance is not what the application would have
            // written for a session that reached the server.
            ("@ip", "203.0.113.7"), ("@ua", "LegacyBrowser/1.0"));
    }

    private static Task AddPageAsync(
        SqlConnection connection, Guid id, Guid organizerId, string slug, string title,
        string? description, int durationMinutes, bool isActive, DateTime created)
        => ExecAsync(connection,
            """
            INSERT INTO BookingPages
                (Id, OrganizerId, Slug, Title, Description, DurationMinutes,
                 BufferBeforeMinutes, BufferAfterMinutes, IsActive,
                 MinNoticeMinutes, MaxBookingWindowDays, MaxBookingsPerDay, CreatedAt)
            VALUES (@id, @org, @slug, @title, @description, @duration, 0, 0, @active, NULL, NULL, NULL, @created);
            """,
            ("@id", id), ("@org", organizerId), ("@slug", slug), ("@title", title),
            ("@description", description), ("@duration", durationMinutes),
            ("@active", isActive), ("@created", created));

    private static async Task AddScheduleAsync(
        SqlConnection connection, Guid scheduleId, Guid organizerId, string timeZoneId,
        DateTime created, bool wednesdaySplit)
    {
        await ExecAsync(connection,
            "INSERT INTO WorkingSchedules (Id, OrganizerId, TimeZoneId, CreatedAt) VALUES (@id, @org, @tz, @created);",
            ("@id", scheduleId), ("@org", organizerId), ("@tz", timeZoneId), ("@created", created));

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var isWorkday = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

            // Derived from the schedule id so the ids stay deterministic without
            // needing seven more constants per organizer.
            var dayId = DeterministicId(scheduleId, (int)day);

            await ExecAsync(connection,
                "INSERT INTO WorkingDays (Id, WorkingScheduleId, DayOfWeek, IsEnabled) VALUES (@id, @schedule, @day, @enabled);",
                ("@id", dayId), ("@schedule", scheduleId), ("@day", (byte)day), ("@enabled", isWorkday));

            if (!isWorkday) continue;

            // WorkingDayIntervals.Id is an IDENTITY surrogate, so it is not
            // supplied here - the owned collection is keyed (WorkingDayId, Id).
            if (wednesdaySplit && day == DayOfWeek.Wednesday)
            {
                await AddIntervalAsync(connection, dayId, new TimeOnly(9, 0), new TimeOnly(12, 0));
                await AddIntervalAsync(connection, dayId, new TimeOnly(13, 0), new TimeOnly(17, 0));
            }
            else
            {
                await AddIntervalAsync(connection, dayId, new TimeOnly(9, 0), new TimeOnly(17, 0));
            }
        }
    }

    private static Task AddIntervalAsync(SqlConnection connection, Guid workingDayId, TimeOnly start, TimeOnly end)
        => ExecAsync(connection,
            "INSERT INTO WorkingDayIntervals (WorkingDayId, StartTime, EndTime) VALUES (@day, @start, @end);",
            ("@day", workingDayId), ("@start", start), ("@end", end));

    private static Task AddExceptionAsync(
        SqlConnection connection, Guid id, Guid organizerId, DateOnly date,
        TimeOnly? start, TimeOnly? end, byte type, string? reason, DateTime created)
        // No EndDate column: that is the point. This INSERT is only writable
        // against the legacy schema, and would fail on the current one - which is
        // the strongest possible statement that the database really is old.
        => ExecAsync(connection,
            """
            INSERT INTO AvailabilityExceptions (Id, OrganizerId, Date, StartTime, EndTime, Type, Reason, CreatedAt)
            VALUES (@id, @org, @date, @start, @end, @type, @reason, @created);
            """,
            ("@id", id), ("@org", organizerId), ("@date", date),
            ("@start", start), ("@end", end), ("@type", type), ("@reason", reason), ("@created", created));

    /// <summary>A stable Guid from a seed Guid and an ordinal - no randomness, no collisions in this seed.</summary>
    public static Guid DeterministicId(Guid seed, int ordinal)
    {
        var bytes = seed.ToByteArray();
        bytes[15] = (byte)(bytes[15] ^ (ordinal + 0x40));
        return new Guid(bytes);
    }

    private static async Task ExecAsync(SqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, ToSqlValue(value));
        }
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// DateOnly/TimeOnly are converted here rather than passed straight to
    /// <c>AddWithValue</c>: whether Microsoft.Data.SqlClient maps them natively
    /// depends on its version, and the SQL Server <c>date</c>/<c>time</c> types
    /// have always accepted DateTime/TimeSpan. Nothing about the value changes -
    /// a DateOnly becomes midnight on the same day, which is what a <c>date</c>
    /// column stores either way.
    /// </summary>
    private static object ToSqlValue(object? value) => value switch
    {
        null => DBNull.Value,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        TimeOnly time => time.ToTimeSpan(),
        _ => value,
    };
}
