namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// Which migration this suite calls "the old version of BookingTracker", and
/// why that one.
///
/// The whole point of this project is that an empty database migrated to the
/// latest schema proves only that the schema can be CREATED. It says nothing
/// about upgrading a database that already holds rows, which is the only kind of
/// upgrade that can lose anything. So the database is built at
/// <see cref="Baseline"/>, populated, and only then migrated forward.
///
/// **Why 20260803094536_AddCalendarProductionHardening.** The migration history
/// was audited for every operation whose behaviour depends on rows already being
/// present. Exactly four such classes exist in the whole history, and this
/// baseline is the latest migration that leaves ALL FOUR still ahead of it:
///
///   1. A data backfill - <c>AddAvailabilityExceptionEndDate</c> is the only
///      migration in the repository containing <c>migrationBuilder.Sql</c>. It
///      adds a non-nullable EndDate whose scaffolded default is year 1 and then
///      repairs it with <c>UPDATE [AvailabilityExceptions] SET [EndDate] = [Date]</c>.
///      Without that statement every pre-existing exception would end up with
///      EndDate &lt; Date, and <c>AvailabilityException.Covers</c> would report
///      that none of them applies to any date - silently un-blocking every date
///      an organizer had deliberately blocked. Nothing else in the history can
///      corrupt data this quietly, which makes it the highest-value target.
///
///   2. A narrowed column - <c>AddBookingSessionMessageMaxLength</c> is the only
///      <c>AlterColumn</c> in the history, taking BookingSessions.Message from
///      nvarchar(max) to nvarchar(2000) on a table that already has rows.
///
///   3. A non-nullable column added to a populated table -
///      <c>AddBookingMeetingProvider</c> (BookingPages.MeetingProvider, default 0)
///      and <c>AddBookingReminders</c> (NotificationSettings.NotifyOrganizerOnReminderSent,
///      default false).
///
///   4. An index dropped and recreated over existing rows -
///      <c>AddAvailabilityExceptionEndDate</c> again, and
///      <c>NarrowBookingConflictLockFootprint</c>, which replaces
///      IX_BookingSessions_BookingPageId_Status with the covering index the
///      claim path depends on.
///
/// Going one migration further back buys nothing (AddCalendarProductionHardening
/// only adds columns with defaults to CalendarConnections); going forward loses
/// one of the four.
/// </summary>
public static class LegacySchema
{
    /// <summary>
    /// The migration the legacy database is built at. Full id rather than the
    /// bare name, so it cannot be confused with a differently-timestamped
    /// migration of the same name.
    /// </summary>
    public const string Baseline = "20260803094536_AddCalendarProductionHardening";

    /// <summary>
    /// The migrations this suite actually exercises: everything applied on top of
    /// <see cref="Baseline"/>, in order.
    ///
    /// Listed explicitly rather than derived, so that adding a migration to the
    /// repository without deciding whether it carries data risk makes
    /// <c>MigrationRangeIsTheOneThisSuiteClaimsToTest</c> fail rather than
    /// silently widening what this file claims.
    /// </summary>
    public static readonly IReadOnlyList<string> Applied =
    [
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
}
