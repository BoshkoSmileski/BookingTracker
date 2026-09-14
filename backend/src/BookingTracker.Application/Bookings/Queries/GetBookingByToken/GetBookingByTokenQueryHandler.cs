using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Bookings.Queries.GetBookingByToken;

public class GetBookingByTokenQueryHandler : IRequestHandler<GetBookingByTokenQuery, PublicBookingDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingByTokenQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<PublicBookingDto> Handle(GetBookingByTokenQuery request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicToken == request.PublicToken, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.PublicToken);

        var page = await _db.BookingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.BookingPageId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), session.BookingPageId);

        var organizer = await _db.Organizers.AsNoTracking()
            .Where(o => o.Id == page.OrganizerId)
            .Select(o => new { o.Name, o.Email })
            .FirstOrDefaultAsync(cancellationToken);

        var timeZoneId = await _db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == page.OrganizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        // Resolved once, here, and used for both the is-it-still-live test and
        // the DTO. That test always needed the instant; it used to compute it
        // and throw it away, which left the guest's own page with nothing but
        // organizer-local wall clock to build a calendar file from.
        var startUtc = ToUtc(session.SelectedDate, session.SelectedTime, timeZoneId);
        var endUtc = startUtc?.AddMinutes(page.DurationMinutes);
        var canManage = session.Status == BookingSessionStatus.Submitted && startUtc > DateTime.UtcNow;

        // Only rows that are still Scheduled, and only while the booking is
        // still live - the two together are what make "we'll remind you" a
        // statement about this booking rather than about the organizer's
        // settings. A cancelled booking's reminders are already Cancelled, so
        // this would be empty anyway; the guard is belt-and-braces and saves the
        // query on every terminal read.
        var reminderLeadMinutes = canManage
            ? await _db.BookingReminders.AsNoTracking()
                .Where(r => r.BookingSessionId == session.Id && r.Status == BookingReminderStatus.Scheduled)
                .Select(r => r.MinutesBeforeEvent)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync(cancellationToken)
            : [];

        return new PublicBookingDto(
            session.BookingReference!,
            page.Slug,
            organizer?.Name ?? "Organizer",
            organizer?.Email ?? string.Empty,
            page.Title,
            page.DurationMinutes,
            session.SelectedDate,
            session.SelectedTime,
            // Already loaded above for the is-it-in-the-future test; sent on
            // rather than discarded, so the guest's page can label the clock
            // the two values above are on.
            timeZoneId,
            startUtc,
            endUtc,
            session.Status.ToString(),
            session.Name,
            session.Email,
            CanCancel: canManage,
            CanReschedule: canManage,
            // Same condition as cancel/reschedule, and for the same reason: a
            // booking that is cancelled or already over has no meeting left to
            // join. The session keeps the URL as history either way - this
            // decides only whether a guest is offered a button that would take
            // them to a Google event that has very likely been deleted.
            MeetingProvider: canManage ? session.MeetingProvider?.ToString() : null,
            MeetingUrl: canManage ? session.MeetingUrl : null,
            ReminderLeadMinutes: reminderLeadMinutes);
    }

    /// <summary>
    /// The booking's organizer-local wall clock as a real instant, or null when
    /// it has no date/time yet. Falls back to UTC for an unrecognized zone, the
    /// same posture the rest of this codebase takes - a booking that cannot be
    /// located on a clock must still be readable, not a 500.
    /// </summary>
    private static DateTime? ToUtc(DateOnly? date, TimeOnly? time, string timeZoneId)
    {
        if (date is null || time is null) return null;

        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { timeZone = TimeZoneInfo.Utc; }

        var localDateTime = DateTime.SpecifyKind(date.Value.ToDateTime(time.Value), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
    }
}
