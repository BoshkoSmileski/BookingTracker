using BookingTracker.Application.BookingPages.Common;
using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.CreateBookingPage;

public class CreateBookingPageCommandHandler : IRequestHandler<CreateBookingPageCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public CreateBookingPageCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(CreateBookingPageCommand request, CancellationToken cancellationToken)
    {
        var slug = await ResolveUniqueSlugAsync(SlugGenerator.Slugify(request.Title), cancellationToken);

        var page = BookingPage.Create(
            request.OrganizerId,
            slug,
            request.Title,
            request.DurationMinutes,
            request.BufferBeforeMinutes,
            request.BufferAfterMinutes,
            request.Description,
            request.MinNoticeMinutes,
            request.MaxBookingWindowDays,
            request.MaxBookingsPerDay);

        _db.BookingPages.Add(page);
        await SeedDefaultWorkingScheduleIfNoneAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }

    /// <summary>
    /// A brand-new organizer's first booking page arrives with Monday-Friday
    /// 09:00-17:00 already set, so it is bookable the moment it exists.
    ///
    /// This is the onboarding boundary, not registration and not the Working
    /// hours screen. Registration is too early - an organizer with no booking
    /// page has nothing to be available *for*, and no zone has been observed
    /// yet. The editor is too late and would be the wrong shape: defaults
    /// applied on load are unsaved state that has to be re-derived on every
    /// visit, and one visit that saved nothing would leave the page unbookable
    /// with the screen claiming otherwise.
    ///
    /// Strictly create-only. An organizer who already has a schedule keeps it
    /// exactly as it is, including one where every day is switched off - that
    /// is a decision they made, not an absence to be filled in.
    /// </summary>
    private async Task SeedDefaultWorkingScheduleIfNoneAsync(
        CreateBookingPageCommand request, CancellationToken cancellationToken)
    {
        var alreadyHasSchedule = await _db.WorkingSchedules
            .AnyAsync(s => s.OrganizerId == request.OrganizerId, cancellationToken);
        if (alreadyHasSchedule) return;

        var schedule = WorkingSchedule.Create(
            request.OrganizerId, WorkingScheduleDefaults.ResolveTimeZoneId(request.TimeZoneId));

        _db.WorkingSchedules.Add(schedule);
        _db.WorkingDays.AddRange(WorkingScheduleDefaults.CreateDays(schedule.Id));
    }

    private async Task<string> ResolveUniqueSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        var slug = baseSlug;
        var suffix = 2;
        while (await _db.BookingPages.AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }
}
