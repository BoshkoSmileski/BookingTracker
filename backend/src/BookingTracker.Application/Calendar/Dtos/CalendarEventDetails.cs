namespace BookingTracker.Application.Calendar.Dtos;

/// <summary>Everything a provider needs to create/update an external calendar event - times are always UTC, TimeZoneId is passed through only so the provider can render the event in the organizer's local time.</summary>
public record CalendarEventDetails(
    string Title,
    string Description,
    DateTime StartUtc,
    DateTime EndUtc,
    string TimeZoneId,
    string OrganizerEmail,
    string GuestName,
    string GuestEmail,
    string BookingReference,
    string? Location,
    /// <summary>Public self-service link (view/cancel/reschedule) - included in the event description when available, so the organizer can hand it to the guest straight from their calendar.</summary>
    string? BookingManagementUrl,
    /// <summary>Minutes before the event to show a popup reminder. Null = use the calendar's own default reminders.</summary>
    int? ReminderMinutes,
    /// <summary>Google Calendar event visibility: "default", "public", or "private".</summary>
    string Visibility,
    /// <summary>
    /// Ask the provider to attach a video conference to this event, so the
    /// provider generates the join link itself rather than anything here
    /// inventing one.
    ///
    /// A boolean rather than a MeetingProviderType because which conferencing
    /// product an external calendar can create is the calendar's business, not
    /// the caller's: Google Calendar makes a Meet, an Outlook calendar would
    /// make a Teams meeting. The booking page's MeetingProviderType decides
    /// *whether* to ask; the provider decides what that means.
    /// </summary>
    bool RequestConference = false);
