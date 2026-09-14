using System.Globalization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications.Templates;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingTracker.Application.Notifications;

/// <summary>
/// See IEmailNotificationService for the "why" - this is the one
/// implementation, and the only place that (a) reads NotificationSettings to
/// decide who should be notified and (b) turns a booking event into
/// EmailNotification rows via IEmailTemplateRenderer. It never sends
/// anything itself; EmailQueueProcessor (Infrastructure) does that later.
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ICalendarInvitationGenerator _invitationGenerator;
    private readonly IFrontendLinkBuilder _linkBuilder;
    private readonly EmailNotificationSettings _settings;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IBookingTrackerDbContext db,
        IEmailTemplateRenderer renderer,
        ICalendarInvitationGenerator invitationGenerator,
        IFrontendLinkBuilder linkBuilder,
        IOptions<EmailNotificationSettings> settings,
        ILogger<EmailNotificationService> logger)
    {
        _db = db;
        _renderer = renderer;
        _invitationGenerator = invitationGenerator;
        _linkBuilder = linkBuilder;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> QueueBookingConfirmedAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        var info = await LoadInfoAsync(session, cancellationToken);
        if (info is null) return false;

        var settings = await GetOrDefaultSettingsAsync(info.Organizer.Id, cancellationToken);
        // One invitation shared by both recipients: it is the same event, with the
        // organizer as ORGANIZER and the guest as ATTENDEE either way.
        var invitation = TryBuildInvitation(info, cancelled: false);
        var queuedAny = false;
        var queuedGuest = false;

        if (settings.NotifyGuestOnBooking)
        {
            var content = _renderer.RenderBookingConfirmation(BuildContext(info, info.Session.Name!));
            Enqueue(info, EmailNotificationType.BookingConfirmation, info.Session.Email!, info.Session.Name!, content, "BookingConfirmation", invitation);
            queuedAny = queuedGuest = true;
        }

        if (settings.NotifyOrganizerOnBooking)
        {
            var content = _renderer.RenderOrganizerNewBooking(BuildContext(info, info.Organizer.Name), info.Session.Email!, info.Session.Phone);
            Enqueue(info, EmailNotificationType.OrganizerNewBooking, info.Organizer.Email, info.Organizer.Name, content, "OrganizerNotification", invitation);
            queuedAny = true;
        }

        // Reported only after the rows are durably saved. A SaveChanges that
        // throws leaves nothing queued, and the caller must not then tell a
        // guest an email is on its way.
        if (queuedAny) await _db.SaveChangesAsync(cancellationToken);
        return queuedGuest;
    }

    public async Task<bool> QueueBookingCancelledAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        var info = await LoadInfoAsync(session, cancellationToken);
        if (info is null) return false;

        var settings = await GetOrDefaultSettingsAsync(info.Organizer.Id, cancellationToken);
        var reason = info.Session.CancellationReason;
        // CANCEL at a higher SEQUENCE, same UID - this is what removes the event from
        // the recipient's calendar instead of leaving a stale one behind.
        var invitation = TryBuildInvitation(info, cancelled: true);
        var queuedAny = false;
        var queuedGuest = false;

        if (settings.NotifyGuestOnBooking)
        {
            var content = _renderer.RenderCancellationConfirmation(BuildContext(info, info.Session.Name!), reason, recipientIsOrganizer: false);
            Enqueue(info, EmailNotificationType.CancellationConfirmation, info.Session.Email!, info.Session.Name!, content, "CancellationConfirmation", invitation);
            queuedAny = queuedGuest = true;
        }

        if (settings.NotifyOrganizerOnBooking)
        {
            var content = _renderer.RenderCancellationConfirmation(BuildContext(info, info.Organizer.Name), reason, recipientIsOrganizer: true);
            Enqueue(info, EmailNotificationType.OrganizerCancellationNotice, info.Organizer.Email, info.Organizer.Name, content, "OrganizerCancellationNotice", invitation);
            queuedAny = true;
        }

        if (queuedAny) await _db.SaveChangesAsync(cancellationToken);
        return queuedGuest;
    }

    public async Task<bool> QueueBookingRescheduledAsync(
        BookingSession session, DateOnly? previousDate, TimeOnly? previousTime, CancellationToken cancellationToken = default)
    {
        var info = await LoadInfoAsync(session, cancellationToken);
        if (info is null) return false;

        var settings = await GetOrDefaultSettingsAsync(info.Organizer.Id, cancellationToken);
        var oldDateLabel = previousDate?.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) ?? "—";
        var oldTimeLabel = previousTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
        // Same UID, new times, SEQUENCE bumped by the reschedule count - an UPDATE of
        // the existing event, never a second one.
        var invitation = TryBuildInvitation(info, cancelled: false);
        var queuedAny = false;
        var queuedGuest = false;

        if (settings.NotifyGuestOnBooking)
        {
            var content = _renderer.RenderRescheduleConfirmation(BuildContext(info, info.Session.Name!), oldDateLabel, oldTimeLabel, recipientIsOrganizer: false);
            Enqueue(info, EmailNotificationType.RescheduleConfirmation, info.Session.Email!, info.Session.Name!, content, "RescheduleConfirmation", invitation);
            queuedAny = queuedGuest = true;
        }

        if (settings.NotifyOrganizerOnBooking)
        {
            var content = _renderer.RenderRescheduleConfirmation(BuildContext(info, info.Organizer.Name), oldDateLabel, oldTimeLabel, recipientIsOrganizer: true);
            Enqueue(info, EmailNotificationType.OrganizerRescheduleNotice, info.Organizer.Email, info.Organizer.Name, content, "OrganizerRescheduleNotice", invitation);
            queuedAny = true;
        }

        if (queuedAny) await _db.SaveChangesAsync(cancellationToken);
        return queuedGuest;
    }

    public async Task<Guid?> QueueReminderAsync(
        BookingSession session, string windowKey, string windowLabel, CancellationToken cancellationToken = default)
    {
        var info = await LoadInfoAsync(session, cancellationToken);
        if (info is null) return null;

        var guestContent = _renderer.RenderReminder(BuildContext(info, info.Session.Name!), windowLabel);
        var guestNotification = Enqueue(
            info, EmailNotificationType.Reminder, info.Session.Email!, info.Session.Name!, guestContent, windowKey);

        var settings = await GetOrDefaultSettingsAsync(info.Organizer.Id, cancellationToken);
        if (settings.NotifyOrganizerOnReminderSent)
        {
            // Queued alongside the guest's copy rather than after it actually sends:
            // making it conditional on delivery would require the queue processor to
            // know about reminders, re-coupling the two halves this design keeps apart.
            // The trade-off is documented - if the guest's copy exhausts its retries,
            // the organizer's "reminder sent" notice may still go out.
            var organizerContent = _renderer.RenderReminder(BuildContext(info, info.Organizer.Name), windowLabel);
            Enqueue(
                info, EmailNotificationType.OrganizerReminderNotice, info.Organizer.Email, info.Organizer.Name,
                organizerContent, windowKey);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return guestNotification.Id;
    }

    public async Task QueueResendBookingConfirmationAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        var info = await LoadInfoAsync(session, cancellationToken);
        if (info is null) return;

        // A resend reflects the booking as it stands now: a cancelled booking resends
        // the CANCEL, so a recipient who lost the original still ends up with the event
        // removed rather than re-added.
        var invitation = TryBuildInvitation(info, cancelled: info.Session.Status == BookingSessionStatus.Cancelled);

        var content = _renderer.RenderBookingConfirmation(BuildContext(info, info.Session.Name!));
        Enqueue(info, EmailNotificationType.BookingConfirmation, info.Session.Email!, info.Session.Name!, content, "BookingConfirmation", invitation);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private EmailNotification Enqueue(
        BookingEmailInfo info, EmailNotificationType type, string toEmail, string toName, EmailContent content, string eventLogFieldName,
        CalendarInvitation? invitation = null)
    {
        var notification = EmailNotification.Create(
            info.Session.Id, type, toEmail, toName, content.Subject, content.HtmlBody, content.TextBody,
            eventLogFieldName, _settings.MaxRetries,
            invitation?.Content, invitation?.FileName, invitation?.Method);
        _db.EmailNotifications.Add(notification);
        _logger.LogInformation(
            "Queued {NotificationType} email to {ToEmail} for session {SessionId} ({Invitation}).",
            type, toEmail, info.Session.Id, invitation is null ? "no invitation" : $"{invitation.Method} invitation");
        return notification;
    }

    /// <summary>
    /// Builds the invitation for a booking email, or null if it cannot be built.
    /// Best-effort by design and consistent with the rest of the notification
    /// path: a malformed invitation must never stop the email - let alone the
    /// booking - from going out, so a failure is logged and the email is queued
    /// without an attachment.
    /// </summary>
    private CalendarInvitation? TryBuildInvitation(BookingEmailInfo info, bool cancelled)
    {
        try
        {
            var viewUrl = _linkBuilder.BuildManageBookingUrl(info.Session.PublicToken!);
            return cancelled
                ? _invitationGenerator.CreateCancellation(info.Session, info.Page, info.Organizer, info.TimeZoneId, viewUrl)
                : _invitationGenerator.CreateRequest(info.Session, info.Page, info.Organizer, info.TimeZoneId, viewUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build calendar invitation for session {SessionId}; queueing email without one.", info.Session.Id);
            return null;
        }
    }

    private BookingEmailContext BuildContext(BookingEmailInfo info, string recipientName)
    {
        var dateLabel = info.Session.SelectedDate!.Value.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        var timeLabel = info.Session.SelectedTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

        return new BookingEmailContext(
            RecipientName: recipientName,
            OrganizerName: info.Organizer.Name,
            GuestName: info.Session.Name!,
            ServiceTitle: info.Page.Title,
            DateLabel: dateLabel,
            TimeLabel: timeLabel,
            TimeZone: info.TimeZoneId,
            DurationMinutes: info.Page.DurationMinutes,
            // BookingPage has no location field yet - see BookingEmailContext's doc comment.
            Location: null,
            Notes: info.Session.Message,
            BookingReference: info.Session.BookingReference!,
            ViewUrl: _linkBuilder.BuildManageBookingUrl(info.Session.PublicToken!),
            CancelUrl: _linkBuilder.BuildCancelBookingUrl(info.Session.PublicToken!),
            RescheduleUrl: _linkBuilder.BuildRescheduleBookingUrl(info.Session.PublicToken!),
            Answers: PairAnswersWithLabels(info),
            // Straight off the session - the same stored value the ICS
            // attachment, the organizer's session detail and the guest's manage
            // page all read. Read from the SESSION and not from
            // info.Page.MeetingProvider on purpose: the page's setting is what
            // future bookings get, so a page switched to In person after this
            // booking was made must not blank a link the guest already holds.
            MeetingLabel: DescribeMeeting(info.Session.MeetingProvider),
            MeetingUrl: info.Session.MeetingUrl,
            // Built for every context rather than only the cancellation one:
            // the page slug is already loaded, and a template that decides
            // whether to draw an action is easier to keep honest than a builder
            // that decides whether to supply the data for it. Only
            // CancellationConfirmation renders it today.
            BookAgainUrl: _linkBuilder.BuildBookingPageUrl(info.Page.Slug));
    }

    /// <summary>
    /// The one place a MeetingProviderType becomes words a recipient reads.
    /// Returns null when there is no meeting, which is what makes every
    /// template omit its Join section rather than render an empty one.
    /// </summary>
    private static string? DescribeMeeting(MeetingProviderType? provider) => provider switch
    {
        MeetingProviderType.GoogleMeet => "Google Meet",
        _ => null,
    };

    /// <summary>
    /// Turns the session's field-id-keyed answers into labelled rows in the
    /// organizer's display order. The one place that join is made, so no
    /// template needs to know a BookingSessionAnswer stores an id.
    ///
    /// Answers whose field has since been deleted are skipped rather than shown
    /// with a placeholder label: the booking keeps them as history (see
    /// BookingPage.RemoveFormField), but an email captioned "(removed field)"
    /// helps nobody.
    /// </summary>
    private static IReadOnlyList<(string Label, string Value)> PairAnswersWithLabels(BookingEmailInfo info)
    {
        if (info.Session.Answers.Count == 0) return [];

        return info.Page.FormFields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => (f.Label, Value: info.Session.Answers.FirstOrDefault(a => a.BookingFormFieldId == f.Id)?.Value))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => (pair.Label, pair.Value!))
            .ToList();
    }

    private async Task<BookingEmailInfo?> LoadInfoAsync(BookingSession session, CancellationToken cancellationToken)
    {
        if (session.SelectedDate is null || session.SelectedTime is null || session.Name is null
            || session.Email is null || session.PublicToken is null || session.BookingReference is null)
        {
            _logger.LogWarning("Skipped queueing notification emails for session {SessionId} - booking is not in a confirmable state.", session.Id);
            return null;
        }

        var page = await _db.BookingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.BookingPageId, cancellationToken);
        if (page is null) return null;

        var organizer = await _db.Organizers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == page.OrganizerId, cancellationToken);
        if (organizer is null) return null;

        var timeZoneId = await _db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == organizer.Id)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        return new BookingEmailInfo(session, page, organizer, timeZoneId);
    }

    private async Task<NotificationSettings> GetOrDefaultSettingsAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var settings = await _db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizerId == organizerId, cancellationToken);
        return settings ?? NotificationSettings.CreateDefault(organizerId);
    }

    private sealed record BookingEmailInfo(BookingSession Session, BookingPage Page, Organizer Organizer, string TimeZoneId);
}
