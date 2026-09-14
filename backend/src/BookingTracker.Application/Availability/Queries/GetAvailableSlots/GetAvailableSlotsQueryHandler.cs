using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Services;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Application.Availability.Queries.GetAvailableSlots;

/// <summary>
/// Orchestrates I/O (load working hours, exceptions, existing bookings) and
/// hands everything to the pure SlotGenerationService domain service - this
/// handler contains no scheduling logic of its own, only data assembly.
/// </summary>
public class GetAvailableSlotsQueryHandler : IRequestHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly ILogger<GetAvailableSlotsQueryHandler> _logger;

    public GetAvailableSlotsQueryHandler(
        IBookingTrackerDbContext db, ICalendarSyncService calendarSyncService, ILogger<GetAvailableSlotsQueryHandler> logger)
    {
        _db = db;
        _calendarSyncService = calendarSyncService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableSlotDto>> Handle(GetAvailableSlotsQuery request, CancellationToken cancellationToken)
    {
        var page = await _db.BookingPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.BookingPageId && p.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), request.BookingPageId);

        var schedule = await _db.WorkingSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizerId == page.OrganizerId, cancellationToken);

        // No schedule configured yet - nothing is bookable, but that's a normal state, not an error.
        if (schedule is null) return [];

        // A page's MaxBookingWindowDays caps how far into the future it can be booked,
        // independent of (and never wider than) the caller-requested range.
        var effectiveToDate = page.MaxBookingWindowDays is { } windowDays
            ? Min(request.ToDate, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(windowDays))
            : request.ToDate;

        var workingDays = await _db.WorkingDays
            .AsNoTracking()
            .Where(d => d.WorkingScheduleId == schedule.Id)
            .ToListAsync(cancellationToken);

        // Overlap, not containment: an exception spans a date range, so a vacation
        // that started before this window and runs into it must still be loaded.
        var exceptions = await _db.AvailabilityExceptions
            .AsNoTracking()
            .Where(e => e.OrganizerId == page.OrganizerId && e.Date <= effectiveToDate && e.EndDate >= request.FromDate)
            .ToListAsync(cancellationToken);

        // Date-specific opening hours. A plain range test, not an overlap one:
        // an override is a single day, so it cannot reach into the window from
        // outside it the way a multi-day exception can.
        var overrides = await _db.AvailabilityOverrides
            .AsNoTracking()
            .Where(o => o.OrganizerId == page.OrganizerId && o.Date >= request.FromDate && o.Date <= effectiveToDate)
            .ToListAsync(cancellationToken);

        // Conflicts are checked across every page belonging to this organizer, not just
        // this one - one organizer can't be double-booked just because two different
        // booking pages happen to offer different services.
        var existingBookings = await (
            from session in _db.BookingSessions.AsNoTracking()
            join bookedPage in _db.BookingPages.AsNoTracking() on session.BookingPageId equals bookedPage.Id
            where bookedPage.OrganizerId == page.OrganizerId
                && session.Status == BookingSessionStatus.Submitted
                && session.SelectedDate != null && session.SelectedTime != null
                && session.SelectedDate >= request.FromDate && session.SelectedDate <= effectiveToDate
            select new
            {
                Date = session.SelectedDate!.Value,
                Time = session.SelectedTime!.Value,
                bookedPage.DurationMinutes,
                bookedPage.BufferBeforeMinutes,
                bookedPage.BufferAfterMinutes
            }).ToListAsync(cancellationToken);

        var occupiedIntervals = existingBookings
            .Select(b => OccupiedInterval.FromBookingWindow(b.Date, b.Time, b.DurationMinutes, b.BufferBeforeMinutes, b.BufferAfterMinutes))
            .ToList();

        var timeZone = ResolveTimeZone(schedule.TimeZoneId);

        var slots = SlotGenerationService.GenerateSlots(
            workingDays,
            exceptions,
            occupiedIntervals,
            timeZone,
            page.DurationMinutes,
            page.BufferBeforeMinutes,
            page.BufferAfterMinutes,
            request.FromDate,
            effectiveToDate,
            DateTime.UtcNow,
            minNoticeMinutes: page.MinNoticeMinutes ?? 0,
            overrides: overrides);

        if (page.MaxBookingsPerDay is { } maxPerDay)
        {
            // This page's OWN existing bookings only - existingBookings/occupiedIntervals above
            // deliberately span every page the organizer owns (for double-booking prevention),
            // which is not the right denominator for a per-page daily cap.
            var thisPageBookedDates = await _db.BookingSessions
                .AsNoTracking()
                .Where(s => s.BookingPageId == page.Id
                    && s.Status == BookingSessionStatus.Submitted
                    && s.SelectedDate != null
                    && s.SelectedDate >= request.FromDate && s.SelectedDate <= effectiveToDate)
                .GroupBy(s => s.SelectedDate!.Value)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Date, g => g.Count, cancellationToken);

            slots = slots.Where(s => thisPageBookedDates.GetValueOrDefault(s.Date) < maxPerDay).ToList();
        }

        // Google Calendar is just another availability source, layered on top of the same
        // working-schedule/exceptions/existing-bookings slots already computed above -
        // GetBusyIntervalsAsync itself is fail-open (never throws, returns [] on any
        // provider/connection problem), so an outage here degrades to "no calendar
        // filtering" rather than breaking the public booking page.
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(request.FromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timeZone);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(effectiveToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), timeZone);

        var busyIntervals = await _calendarSyncService.GetBusyIntervalsAsync(page.OrganizerId, rangeStartUtc, rangeEndUtc, cancellationToken);
        _logger.LogInformation(
            "Calendar busy-interval check for organizer {OrganizerId}: {BusyCount} busy interval(s) fetched for {RangeStart:o}..{RangeEnd:o}.",
            page.OrganizerId, busyIntervals.Count, rangeStartUtc, rangeEndUtc);

        if (busyIntervals.Count > 0)
        {
            var beforeCount = slots.Count;
            slots = slots.Where(s => !busyIntervals.Any(b => b.StartUtc < s.EndUtc && b.EndUtc > s.StartUtc)).ToList();
            _logger.LogInformation(
                "Calendar busy intervals removed {RemovedCount} of {BeforeCount} candidate slot(s) for organizer {OrganizerId}.",
                beforeCount - slots.Count, beforeCount, page.OrganizerId);
        }

        return slots.Select(s => s.ToDto()).ToList();
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
