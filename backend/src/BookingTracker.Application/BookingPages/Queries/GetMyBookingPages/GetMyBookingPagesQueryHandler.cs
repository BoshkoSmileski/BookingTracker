using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Queries.GetMyBookingPages;

public class GetMyBookingPagesQueryHandler : IRequestHandler<GetMyBookingPagesQuery, IReadOnlyList<BookingPageSummaryDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetMyBookingPagesQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<BookingPageSummaryDto>> Handle(GetMyBookingPagesQuery request, CancellationToken cancellationToken)
    {
        var pages = await _db.BookingPages
            .AsNoTracking()
            .Where(p => p.OrganizerId == request.OrganizerId)
            .OrderBy(p => p.Title)
            .ToListAsync(cancellationToken);

        var pageIds = pages.Select(p => p.Id).ToList();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var nowTime = TimeOnly.FromDateTime(now);

        var upcomingCounts = await _db.BookingSessions
            .AsNoTracking()
            .Where(s => s.Status == BookingSessionStatus.Submitted
                && s.SelectedDate != null && s.SelectedTime != null
                && (s.SelectedDate > today || (s.SelectedDate == today && s.SelectedTime >= nowTime))
                && pageIds.Contains(s.BookingPageId))
            .GroupBy(s => s.BookingPageId)
            .Select(g => new { PageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PageId, g => g.Count, cancellationToken);

        return pages
            .Select(p => p.ToSummaryDto(upcomingCounts.GetValueOrDefault(p.Id)))
            .ToList();
    }
}
