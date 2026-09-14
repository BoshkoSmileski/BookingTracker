namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// What each migration's <c>Down()</c> actually does, read off the migration
/// source rather than inferred from its name - and what the database must look
/// like once it has run.
///
/// This file is the rollback audit written down as data, so the rollback tests
/// assert the audit rather than restating it. A migration added without deciding
/// what rolling it back destroys makes <c>TheRollbackPathIsEveryMigrationAfterInitialCreate</c>
/// fail, which is the same guard <c>LegacySchema.Applied</c> provides for the
/// upgrade path.
/// </summary>
public static class MigrationOrder
{
    /// <summary>
    /// Every migration in the repository, oldest first. Full ids rather than bare
    /// names, so two migrations that happened to share a name could not be
    /// confused.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        "20260730190909_InitialCreate",
        "20260730201333_AddOrganizerAuthentication",
        "20260730204651_AddAvailability",
        "20260730230331_AddBookingPageDescription",
        "20260730234846_AddBookingManagement",
        "20260801183440_AddBookingPageWorkspaceFields",
        "20260802173408_AddCalendarIntegration",
        "20260803094536_AddCalendarProductionHardening",
        "20260803181022_AddBookingSessionMessageMaxLength",
        "20260803225629_AddEmailNotifications",
        "20260804083210_AddBookingReminders",
        "20260804140424_AddCalendarInvitationToEmailNotifications",
        "20260805090948_AddAvailabilityExceptionEndDate",
        "20260805210453_AddBookingFormFields",
        "20260806093352_AddBookingMeetingProvider",
        "20260806104951_AddAvailabilityOverrides",
        "20260812201939_NarrowBookingConflictLockFootprint",
    ];

    /// <summary>
    /// Where the rollback stops, and why it is not zero.
    ///
    /// <c>InitialCreate.Down</c> drops Organizers, BookingPages, BookingSessions
    /// and BookingSessionEvents - i.e. it empties the database completely. There
    /// is nothing to learn from executing it that "DROP TABLE removes a table"
    /// does not already say, and running it would throw away the one thing this
    /// suite is trying to observe: which rows survive a full rollback. So the
    /// target is InitialCreate itself, which stays applied, and the sixteen
    /// migrations above it are rolled back.
    /// </summary>
    public static string RollbackTarget => All[0];

    /// <summary>The sixteen Down migrations this suite executes, newest first - the order EF runs them in.</summary>
    public static IReadOnlyList<RollbackStep> Path => Steps;

    /// <summary>
    /// How much a rollback costs the data.
    ///
    /// The distinction is deliberately about INFORMATION rather than about rows:
    /// <c>AddAvailabilityExceptionEndDate.Down</c> keeps every exception row and
    /// is still destructive, because a multi-day range cannot be reconstructed
    /// from a start date alone.
    /// </summary>
    public enum Impact
    {
        /// <summary>Nothing a user could observe is lost - index shape, or a column widened back.</summary>
        DataPreserving,

        /// <summary>Information that existed only in the dropped structure is gone.</summary>
        Destructive,
    }

    /// <param name="MigrationId">The migration being rolled back.</param>
    /// <param name="Target">Where the database lands afterwards - the migration below it.</param>
    /// <param name="What">What <c>Down()</c> does, from the migration source.</param>
    /// <param name="TablesDropped">Tables that must not exist afterwards.</param>
    /// <param name="ColumnsDropped">Columns that must not exist afterwards.</param>
    /// <param name="IndexesDropped">Indexes that must not exist afterwards.</param>
    /// <param name="IndexesRestored">Indexes the Down migration recreates.</param>
    /// <param name="RecoverableFromEventLog">
    /// Set where the information the rollback drops is ALSO recorded in
    /// BookingSessionEvents, which no Down migration in this path touches. That
    /// makes the loss a loss of the projection rather than of the fact - which is
    /// the event-sourcing claim this project is built on, seen from a direction
    /// nothing else tests. Asserted, not assumed.
    /// </param>
    public sealed record RollbackStep(
        string MigrationId,
        string Target,
        Impact Impact,
        string What,
        IReadOnlyList<string> TablesDropped,
        IReadOnlyList<(string Table, string Column)> ColumnsDropped,
        IReadOnlyList<(string Table, string Index)> IndexesDropped,
        IReadOnlyList<(string Table, string Index)> IndexesRestored,
        string? RecoverableFromEventLog = null)
    {
        public string Name => MigrationId[(MigrationId.IndexOf('_') + 1)..];
    }

    private static readonly RollbackStep[] Steps =
    [
        new("20260812201939_NarrowBookingConflictLockFootprint", "20260806104951_AddAvailabilityOverrides",
            Impact.DataPreserving,
            "drops the covering conflict index and recreates its narrower predecessor",
            TablesDropped: [],
            ColumnsDropped: [],
            IndexesDropped: [("BookingSessions", "IX_BookingSessions_BookingPageId_Status_SelectedDate")],
            IndexesRestored: [("BookingSessions", "IX_BookingSessions_BookingPageId_Status")]),

        new("20260806104951_AddAvailabilityOverrides", "20260806093352_AddBookingMeetingProvider",
            Impact.Destructive,
            "drops date-specific hours and their owned ranges outright",
            TablesDropped: ["AvailabilityOverrideRanges", "AvailabilityOverrides"],
            ColumnsDropped: [], IndexesDropped: [], IndexesRestored: []),

        new("20260806093352_AddBookingMeetingProvider", "20260805210453_AddBookingFormFields",
            Impact.Destructive,
            "drops every booking's meeting link and provider, and the page-level setting",
            TablesDropped: [],
            ColumnsDropped:
            [
                ("BookingSessions", "MeetingProvider"),
                ("BookingSessions", "MeetingUrl"),
                ("BookingPages", "MeetingProvider"),
            ],
            IndexesDropped: [], IndexesRestored: [],
            RecoverableFromEventLog:
                "MeetingUrl is also a MeetingLinkAssigned event (BookingEventType 13), so the link " +
                "survives in BookingSessionEvents even though the projection column is gone."),

        new("20260805210453_AddBookingFormFields", "20260805090948_AddAvailabilityExceptionEndDate",
            Impact.Destructive,
            "drops the organizer's custom questions and every visitor answer to them",
            TablesDropped: ["BookingFormFields", "BookingSessionAnswers"],
            ColumnsDropped: [], IndexesDropped: [], IndexesRestored: [],
            RecoverableFromEventLog:
                "An answer is an ordinary FieldChanged event named custom:{fieldId} (Development " +
                "Rule #31), so every answer survives in BookingSessionEvents. The QUESTION's label " +
                "does not - BookingFormFields is the only place it was ever stored."),

        new("20260805090948_AddAvailabilityExceptionEndDate", "20260804140424_AddCalendarInvitationToEmailNotifications",
            Impact.Destructive,
            "drops AvailabilityExceptions.EndDate, collapsing every multi-day block to its start date",
            TablesDropped: [],
            ColumnsDropped: [("AvailabilityExceptions", "EndDate")],
            IndexesDropped: [("AvailabilityExceptions", "IX_AvailabilityExceptions_OrganizerId_Date_EndDate")],
            IndexesRestored: [("AvailabilityExceptions", "IX_AvailabilityExceptions_OrganizerId_Date")]),

        new("20260804140424_AddCalendarInvitationToEmailNotifications", "20260804083210_AddBookingReminders",
            Impact.Destructive,
            "drops the stored .ics payload, file name and METHOD from queued emails",
            TablesDropped: [],
            ColumnsDropped:
            [
                ("EmailNotifications", "IcsContent"),
                ("EmailNotifications", "IcsFileName"),
                ("EmailNotifications", "IcsMethod"),
            ],
            IndexesDropped: [], IndexesRestored: []),

        new("20260804083210_AddBookingReminders", "20260803225629_AddEmailNotifications",
            Impact.Destructive,
            "drops every scheduled reminder and the organizer-copy toggle",
            TablesDropped: ["BookingReminders"],
            ColumnsDropped: [("NotificationSettings", "NotifyOrganizerOnReminderSent")],
            IndexesDropped: [], IndexesRestored: []),

        new("20260803225629_AddEmailNotifications", "20260803181022_AddBookingSessionMessageMaxLength",
            Impact.Destructive,
            "drops the persisted email queue and every organizer's notification settings",
            TablesDropped: ["EmailNotifications", "NotificationSettings"],
            ColumnsDropped: [], IndexesDropped: [], IndexesRestored: []),

        new("20260803181022_AddBookingSessionMessageMaxLength", "20260803094536_AddCalendarProductionHardening",
            Impact.DataPreserving,
            "widens BookingSessions.Message back to nvarchar(max) - a widening never truncates",
            TablesDropped: [], ColumnsDropped: [], IndexesDropped: [], IndexesRestored: []),

        new("20260803094536_AddCalendarProductionHardening", "20260802173408_AddCalendarIntegration",
            Impact.Destructive,
            "drops the six per-organizer calendar event/sync settings",
            TablesDropped: [],
            ColumnsDropped:
            [
                ("CalendarConnections", "AutoDeleteCancelledBookings"),
                ("CalendarConnections", "AutoUpdateRescheduledBookings"),
                ("CalendarConnections", "DefaultReminderMinutes"),
                ("CalendarConnections", "EventTitleFormat"),
                ("CalendarConnections", "EventVisibility"),
                ("CalendarConnections", "LastFailedSyncAtUtc"),
            ],
            IndexesDropped: [], IndexesRestored: []),

        new("20260802173408_AddCalendarIntegration", "20260801183440_AddBookingPageWorkspaceFields",
            Impact.Destructive,
            "drops the calendar connection and every booking's mapping to its external event",
            TablesDropped: ["CalendarSyncedEvents", "CalendarConnections"],
            ColumnsDropped: [], IndexesDropped: [], IndexesRestored: []),

        new("20260801183440_AddBookingPageWorkspaceFields", "20260730234846_AddBookingManagement",
            Impact.Destructive,
            "drops booking instructions and the three per-page booking limits",
            TablesDropped: ["BookingQuestions"],
            ColumnsDropped:
            [
                ("BookingPages", "MaxBookingWindowDays"),
                ("BookingPages", "MaxBookingsPerDay"),
                ("BookingPages", "MinNoticeMinutes"),
            ],
            IndexesDropped: [], IndexesRestored: []),

        new("20260730234846_AddBookingManagement", "20260730230331_AddBookingPageDescription",
            Impact.Destructive,
            "drops the booking reference, public token, cancellation and reschedule columns",
            TablesDropped: [],
            ColumnsDropped:
            [
                ("BookingSessions", "BookingReference"),
                ("BookingSessions", "CancellationReason"),
                ("BookingSessions", "CancelledAt"),
                ("BookingSessions", "CancelledBy"),
                ("BookingSessions", "PublicToken"),
                ("BookingSessions", "RescheduleCount"),
                ("BookingSessions", "RescheduledAt"),
            ],
            IndexesDropped:
            [
                ("BookingSessions", "IX_BookingSessions_BookingReference"),
                ("BookingSessions", "IX_BookingSessions_PublicToken"),
                ("BookingSessions", "IX_BookingSessions_Status_SelectedDate_SelectedTime"),
            ],
            IndexesRestored: [],
            RecoverableFromEventLog:
                "ALL of it. BookingSubmitted carries the booking reference as its OldValue and the " +
                "PUBLIC TOKEN as its NewValue - both generated values in one event, so that Apply() " +
                "can restore both when replaying - and BookingCancelled carries the reason. The " +
                "first draft of this audit asserted the opposite about the token on the grounds that " +
                "a bearer secret surely would not be logged; the test disagreed, which is what a " +
                "measured audit is for. It is not a defect (Rebuild() could not reconstruct a " +
                "session's manage link without it) but it does mean the event log is as sensitive " +
                "as the token column, and a rollback does not reduce that exposure."),

        new("20260730230331_AddBookingPageDescription", "20260730204651_AddAvailability",
            Impact.Destructive,
            "drops every booking page's description",
            TablesDropped: [],
            ColumnsDropped: [("BookingPages", "Description")],
            IndexesDropped: [], IndexesRestored: []),

        new("20260730204651_AddAvailability", "20260730201333_AddOrganizerAuthentication",
            Impact.Destructive,
            "drops the entire availability model - schedules, days, owned intervals, exceptions - and the buffers",
            TablesDropped: ["AvailabilityExceptions", "WorkingDayIntervals", "WorkingDays", "WorkingSchedules"],
            ColumnsDropped:
            [
                ("BookingPages", "BufferAfterMinutes"),
                ("BookingPages", "BufferBeforeMinutes"),
            ],
            IndexesDropped: [], IndexesRestored: []),

        new("20260730201333_AddOrganizerAuthentication", "20260730190909_InitialCreate",
            Impact.Destructive,
            "drops every refresh token and every organizer's password hash",
            TablesDropped: ["RefreshTokens"],
            ColumnsDropped: [("Organizers", "PasswordHash")],
            IndexesDropped: [("Organizers", "IX_Organizers_Email")],
            IndexesRestored: []),
    ];

    /// <summary>
    /// The four tables <c>InitialCreate</c> creates - the ones that must still be
    /// standing, with their rows, once the rollback finishes.
    /// </summary>
    public static readonly IReadOnlyList<string> InitialCreateTables =
        ["BookingPages", "BookingSessionEvents", "BookingSessions", "Organizers"];
}
