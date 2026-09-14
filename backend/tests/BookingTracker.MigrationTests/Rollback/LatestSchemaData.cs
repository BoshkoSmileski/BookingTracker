using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.Persistence;

namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// The data a rollback is run over: a realistic installation at the CURRENT
/// schema, built through the real domain entities and saved with the real
/// <c>DbContext</c>.
///
/// <b>Unlike <c>LegacyData</c>, this is not raw SQL - and the difference is not a
/// preference.</b> That file has to write a schema the current model cannot
/// express, so parameterised INSERTs are its only option. This one seeds the
/// schema the model was built for, so using anything other than
/// <c>Organizer.Register</c>, <c>BookingSession.Start</c>/<c>Submit</c>,
/// <c>AvailabilityException.Create</c> and friends would be inventing a second
/// way to produce rows that the application itself produces one way. Every row
/// here is therefore valid by construction rather than by inspection - the
/// domain invariants ran.
///
/// <b>Every row exists to be at risk from something.</b> The seed is aimed at the
/// structures the sixteen Down migrations actually touch:
///
///   multi-day exception       the EndDate that AddAvailabilityExceptionEndDate.Down deletes
///   timed exception           proves the same rollback leaves StartTime/EndTime alone
///   override + owned ranges   an owned collection dropped as two tables
///   working days + intervals  a second owned collection, with a split day
///   custom fields + answers   a projection whose facts also live in the event log
///   Google Meet booking       MeetingUrl, likewise
///   submitted/cancelled/      the booking-management columns dropped wholesale
///     rescheduled sessions
///   reminders, notifications, tables dropped outright, with FKs into rows that survive
///     calendar connection
///   2000-character Message    the exact boundary the column narrowing imposes,
///                             which the Down migration widens back
/// </summary>
public static class LatestSchemaData
{
    // Ids are CAPTURED rather than forced, unlike LegacyData's hardcoded set.
    // That file writes rows with raw SQL and can choose their keys; here the
    // entities mint their own, and overwriting one would need either reflection
    // into the domain or a test-only setter on it - and a production hook that
    // exists solely for a test is exactly what this phase must not add. Every id
    // an assertion needs comes back on Seeded instead.
    public const string AdaEmail = "ada@rollback.invalid";
    public const string BenEmail = "ben@rollback.invalid";
    public const string CaraEmail = "cara@rollback.invalid";
    public const string AdaTimeZoneId = "Europe/Skopje";

    public const string MeetPageSlug = "rollback-meet";
    public const string InPersonPageSlug = "rollback-in-person";
    public const string BenPageSlug = "rollback-ben";

    /// <summary>The meeting URL a rollback deletes from BookingSessions and cannot delete from the event log.</summary>
    public const string MeetingUrl = "https://meet.google.com/rol-lbac-kxx";

    /// <summary>Exactly <c>BookingFieldLimits.MessageMaxLength</c> - the widest the narrowed column holds.</summary>
    public static readonly string BoundaryMessage = new('r', BookingFieldLimits.MessageMaxLength);

    public const string CompanyQuestionLabel = "Company";
    public const string CompanyAnswer = "Rollback Industries";
    public const string TopicQuestionLabel = "What would you like to discuss?";
    public const string TopicAnswer = "Whether Down migrations lose anything.";

    /// <summary>
    /// The calendar dates the seed used. Computed relative to now rather than
    /// hardcoded, and captured so every assertion talks
    /// about the same days the rows were written with.
    /// </summary>
    public sealed record Dates(
        DateOnly Booked,
        DateOnly Rescheduled,
        DateOnly MultiDayStart,
        DateOnly MultiDayEnd,
        DateOnly TimedBlock,
        DateOnly SingleDayBlock,
        DateOnly Override);

    public static Dates ResolveDates()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new Dates(
            Booked: NextWeekday(today.AddDays(14)),
            Rescheduled: NextWeekday(today.AddDays(17)),

            // The row this whole phase is about: a block that spans nine days, so
            // "EndDate was dropped" is a visible loss of a range rather than an
            // invisible loss of a duplicate.
            MultiDayStart: NextWeekday(today.AddDays(30)),
            MultiDayEnd: NextWeekday(today.AddDays(30)).AddDays(9),

            TimedBlock: NextWeekday(today.AddDays(45)),
            SingleDayBlock: NextWeekday(today.AddDays(50)),
            Override: NextWeekday(today.AddDays(60)));

        static DateOnly NextWeekday(DateOnly from)
        {
            while (from.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) from = from.AddDays(1);
            return from;
        }
    }

    /// <summary>What the seed produced, for assertions that need a generated value.</summary>
    public sealed record Seeded(
        Dates Dates,
        Guid AdaId,
        Guid BenId,
        Guid CaraId,
        Guid MeetPageId,
        Guid InPersonPageId,
        Guid BenPageId,
        Guid CompanyFieldId,
        Guid TopicFieldId,
        Guid SubmittedSessionId,
        Guid CancelledSessionId,
        Guid RescheduledSessionId,
        Guid AbandonedSessionId,
        Guid ActiveSessionId,
        Guid MultiDayExceptionId,
        Guid TimedExceptionId,
        Guid OverrideId,
        Guid CalendarConnectionId,
        string SubmittedBookingReference,
        string SubmittedPublicToken,
        string CancelledBookingReference);

    public static async Task<Seeded> SeedAsync(BookingTrackerDbContext db, Dates dates)
    {
        var context = new ClientContext("203.0.113.42", "RollbackBrowser/1.0");

        // ---- organizers ------------------------------------------------------
        // Cara deliberately configures nothing: a real installation has these, and
        // she proves no Down migration needs a schedule or a page to exist.
        var ada = Organizer.Register("Ada Rollback", AdaEmail, "hash-ada");
        var ben = Organizer.Register("Ben Rollback", BenEmail, "hash-ben");
        var cara = Organizer.Register("Cara Rollback", CaraEmail, "hash-cara");
        db.Organizers.AddRange(ada, ben, cara);

        // ---- refresh tokens (one live, one already rotated) -------------------
        var live = RefreshToken.Issue(ada.Id, "rollback-refresh-live", DateTime.UtcNow.AddDays(30));
        var rotated = RefreshToken.Issue(ada.Id, "rollback-refresh-rotated", DateTime.UtcNow.AddDays(30));
        rotated.Revoke("rollback-refresh-live");
        db.RefreshTokens.AddRange(live, rotated);

        // ---- booking pages ----------------------------------------------------
        var meetPage = BookingPage.Create(ada.Id, MeetPageSlug, "Rollback consultation", 30, 5, 10);
        meetPage.UpdateDetails("Rollback consultation", "A page whose description a rollback deletes.");
        meetPage.UpdateLimits(minNoticeMinutes: 120, maxBookingWindowDays: 60, maxBookingsPerDay: 8);
        meetPage.UpdateMeetingSettings(MeetingProviderType.GoogleMeet);
        meetPage.AddQuestion("Please have your account number ready.", 0);
        meetPage.AddQuestion("Arrive five minutes early.", 1);
        var companyField = meetPage.AddFormField(CompanyQuestionLabel, BookingFieldType.ShortText, isRequired: true);
        var topicField = meetPage.AddFormField(TopicQuestionLabel, BookingFieldType.LongText, isRequired: false);

        var inPersonPage = BookingPage.Create(ada.Id, InPersonPageSlug, "Rollback workshop", 60, 0, 0);
        inPersonPage.Deactivate();

        var benPage = BookingPage.Create(ben.Id, BenPageSlug, "Ben's intro", 15, 0, 0);

        db.BookingPages.AddRange(meetPage, inPersonPage, benPage);
        await db.SaveChangesAsync();

        // ---- working schedules, with a split day (owned collection) -----------
        var adaSchedule = WorkingSchedule.Create(ada.Id, AdaTimeZoneId);
        var benSchedule = WorkingSchedule.Create(ben.Id, "UTC");
        db.WorkingSchedules.AddRange(adaSchedule, benSchedule);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var open = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

            // Wednesday is two intervals around a lunch break, so WorkingDayIntervals
            // holds more than one child for one owner - which is what makes dropping
            // the owned-collection table a real test rather than a one-row one.
            var adaIntervals = !open
                ? Array.Empty<TimeRange>()
                : day == DayOfWeek.Wednesday
                    ? [TimeRange.Create(new TimeOnly(9, 0), new TimeOnly(12, 0)), TimeRange.Create(new TimeOnly(13, 0), new TimeOnly(17, 0))]
                    : new[] { TimeRange.Create(new TimeOnly(9, 0), new TimeOnly(17, 0)) };

            db.WorkingDays.Add(WorkingDay.Create(adaSchedule.Id, day, open, adaIntervals));
            db.WorkingDays.Add(WorkingDay.Create(
                benSchedule.Id, day, open,
                open ? [TimeRange.Create(new TimeOnly(10, 0), new TimeOnly(16, 0))] : []));
        }

        // ---- availability exceptions -----------------------------------------
        // The multi-day one is the reason this phase exists.
        var multiDay = Domain.Entities.AvailabilityException.Create(
            ada.Id, dates.MultiDayStart, null, null, AvailabilityExceptionType.Vacation,
            "Ten-day vacation", dates.MultiDayEnd);

        var timed = Domain.Entities.AvailabilityException.Create(
            ada.Id, dates.TimedBlock, new TimeOnly(10, 0), new TimeOnly(12, 0),
            AvailabilityExceptionType.Meeting, "Daily standup");

        var singleDay = Domain.Entities.AvailabilityException.Create(
            ada.Id, dates.SingleDayBlock, null, null, AvailabilityExceptionType.Holiday, null);

        var benException = Domain.Entities.AvailabilityException.Create(
            ben.Id, dates.SingleDayBlock, null, null, AvailabilityExceptionType.SickLeave, null);

        db.AvailabilityExceptions.AddRange(multiDay, timed, singleDay, benException);

        // ---- availability override + owned ranges ----------------------------
        var dateOverride = Domain.Entities.AvailabilityOverride.Create(
            ada.Id, dates.Override,
            [TimeRange.Create(new TimeOnly(13, 0), new TimeOnly(15, 0)), TimeRange.Create(new TimeOnly(16, 0), new TimeOnly(18, 0))],
            "Short day");
        db.AvailabilityOverrides.Add(dateOverride);

        // ---- notification settings + calendar connection ----------------------
        var adaSettings = NotificationSettings.CreateDefault(ada.Id);
        adaSettings.UpdateSettings(true, true, true, [15, 60, 1440], notifyOrganizerOnReminderSent: true);
        db.NotificationSettings.Add(adaSettings);
        db.NotificationSettings.Add(NotificationSettings.CreateDefault(ben.Id));

        var connection = CalendarConnection.Connect(
            ada.Id, CalendarProviderType.Google, "ada@gmail.invalid",
            "primary-calendar-id", "Ada's calendar",
            "ciphertext-access", "ciphertext-refresh", DateTime.UtcNow.AddHours(1));
        connection.UpdateEventSettings(
            "{Service Name} with {Guest Name}",
            autoDeleteCancelledBookings: false,
            autoUpdateRescheduledBookings: false,
            defaultReminderMinutes: 30,
            eventVisibility: "private");
        connection.MarkSyncSucceeded(DateTime.UtcNow.AddMinutes(-5));
        db.CalendarConnections.Add(connection);

        await db.SaveChangesAsync();

        // ---- booking sessions, through the real lifecycle ---------------------
        // Driving the aggregate rather than constructing rows is what makes the
        // event log real: every assertion about BookingSessionEvents is then about
        // events the application itself would have written.
        //
        // EVERY returned event is persisted, including the ones from the mutators
        // after Start. BookingSession has no Events navigation - the aggregate
        // returns each event and the caller is what writes it - so dropping one on
        // the floor produces a projection with no log behind it. That is exactly
        // the state this suite would otherwise mistake for "the rollback lost it",
        // and it is how the first version of this seeder was wrong.
        void Log(BookingSessionEvent? @event)
        {
            if (@event is not null) db.BookingSessionEvents.Add(@event);
        }

        var submitted = StartSession(db, meetPage.Id, context, "Grace Guest", "grace@rollback.invalid",
            "+38970000001", BoundaryMessage, dates.Booked, new TimeOnly(10, 0),
            answers: [(companyField.Id, CompanyAnswer), (topicField.Id, TopicAnswer)]);
        Log(submitted.Submit(20, context));
        Log(submitted.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetingUrl));
        Log(submitted.LogEmailSent("BookingConfirmation", "grace@rollback.invalid"));

        var cancelled = StartSession(db, meetPage.Id, context, "Carla Cancelled", "carla@rollback.invalid",
            null, "Changed my mind.", dates.Booked, new TimeOnly(14, 0), answers: []);
        Log(cancelled.Submit(20, context));
        var cancelledReference = cancelled.BookingReference!;
        Log(cancelled.Cancel(CancelledByType.Customer, "Something came up", context));

        var rescheduled = StartSession(db, meetPage.Id, context, "Rob Rescheduled", "rob@rollback.invalid",
            null, null, dates.Booked, new TimeOnly(11, 0), answers: []);
        Log(rescheduled.Submit(20, context));
        Log(rescheduled.Reschedule(dates.Rescheduled, new TimeOnly(15, 0), context));

        var abandoned = StartSession(db, inPersonPage.Id, context, "Ann Abandoned", "ann@rollback.invalid",
            null, null, null, null, answers: []);
        Log(abandoned.Abandon());

        var active = StartSession(db, benPage.Id, context, "Half Filled", null, null, null, null, null, answers: []);

        await db.SaveChangesAsync();

        // ---- calendar synced event + email queue + reminders ------------------
        // Added after the sessions are saved, because each carries an FK to one.
        db.CalendarSyncedEvents.Add(
            CalendarSyncedEvent.Create(submitted.Id, connection.Id, "google-event-id-rollback"));

        // One with a calendar invitation and one without, so the three Ics columns
        // AddCalendarInvitationToEmailNotifications.Down drops are populated on
        // some rows and null on others.
        var confirmation = EmailNotification.Create(
            submitted.Id, EmailNotificationType.BookingConfirmation, "grace@rollback.invalid", "Grace Guest",
            "Your booking is confirmed", "<p>confirmed</p>", "confirmed", "BookingConfirmation", 5,
            icsContent: "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", icsFileName: "invite.ics", icsMethod: "REQUEST");
        confirmation.MarkSent();

        var reminderEmail = EmailNotification.Create(
            submitted.Id, EmailNotificationType.Reminder, "grace@rollback.invalid", "Grace Guest",
            "Reminder", "<p>soon</p>", "soon", "24h", 5);

        db.EmailNotifications.AddRange(confirmation, reminderEmail);

        var meetingStartsAtUtc = dates.Booked.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        var scheduledReminder = BookingReminder.Schedule(submitted.Id, meetPage.Id, 1440, meetingStartsAtUtc);
        var queuedReminder = BookingReminder.Schedule(submitted.Id, meetPage.Id, 60, meetingStartsAtUtc);
        await db.SaveChangesAsync();
        queuedReminder.MarkQueued(reminderEmail.Id);

        db.BookingReminders.AddRange(scheduledReminder, queuedReminder);
        await db.SaveChangesAsync();

        return new Seeded(
            dates,
            ada.Id, ben.Id, cara.Id,
            meetPage.Id, inPersonPage.Id, benPage.Id,
            companyField.Id, topicField.Id,
            submitted.Id, cancelled.Id, rescheduled.Id, abandoned.Id, active.Id,
            multiDay.Id, timed.Id, dateOverride.Id, connection.Id,
            submitted.BookingReference!, submitted.PublicToken!, cancelledReference);
    }

    /// <summary>
    /// Drives one session through the wizard's real event sequence, including
    /// custom-question answers, which are ordinary <c>FieldChanged</c> events named
    /// <c>custom:{fieldId}</c> - the reason a rollback that
    /// drops BookingSessionAnswers does not drop the answers.
    /// </summary>
    private static BookingSession StartSession(
        BookingTrackerDbContext db, Guid pageId, ClientContext context,
        string? name, string? email, string? phone, string? message,
        DateOnly? date, TimeOnly? time,
        IReadOnlyList<(Guid FieldId, string Value)> answers)
    {
        var (session, startEvent) = BookingSession.Start(pageId, context);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startEvent);

        var sequence = 1;
        void Record(BookingSessionEvent @event) => db.BookingSessionEvents.Add(@event);

        if (name is not null) Record(session.ChangeField(BookingFieldNames.Name, name, sequence++, context));
        if (email is not null) Record(session.ChangeField(BookingFieldNames.Email, email, sequence++, context));
        if (phone is not null) Record(session.ChangeField(BookingFieldNames.Phone, phone, sequence++, context));
        if (message is not null) Record(session.ChangeField(BookingFieldNames.Message, message, sequence++, context));

        foreach (var (fieldId, value) in answers)
        {
            Record(session.ChangeField(BookingFieldNames.ForCustomField(fieldId), value, sequence++, context));
        }

        if (date is not null) Record(session.SelectDate(date, sequence++, context));
        if (time is not null) Record(session.SelectTime(time, sequence, context));

        return session;
    }

}
