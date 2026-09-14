using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics.Queries.GetConversionFunnel;

/// <summary>
/// Conversion funnel and abandonment, both read from the existing
/// BookingSessionEvents log - the raw material the thesis's event sourcing was
/// built to produce, with no new tracking added.
///
/// Each step is a COUNT(DISTINCT SessionId) over the event log, restricted to
/// sessions in scope via a semi-join, so the event stream is aggregated in SQL
/// and never materialized.
///
/// Note on step names: "booking page viewed" and "session started" are the same
/// signal in this system - the wizard creates a session on page load
/// (useBookingSessionTracker), so there is no separate view event to count. The
/// funnel reports one entry step rather than inventing a distinction the data
/// cannot support.
/// </summary>
public class GetConversionFunnelQueryHandler : IRequestHandler<GetConversionFunnelQuery, ConversionFunnelDto>
{
    private const string StepStarted = "Page viewed";
    private const string StepDate = "Date selected";
    private const string StepTime = "Time selected";
    private const string StepDetails = "Details entered";
    private const string StepConfirmed = "Booking confirmed";

    /// <summary>Ordered funnel steps. Abandonment's "furthest step reached" uses the same ladder, so the two panels are directly comparable.</summary>
    private static readonly string[] StepOrder = [StepStarted, StepDate, StepTime, StepDetails, StepConfirmed];

    private readonly IBookingTrackerDbContext _db;

    public GetConversionFunnelQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<ConversionFunnelDto> Handle(GetConversionFunnelQuery request, CancellationToken cancellationToken)
    {
        var scope = await AnalyticsScope.ResolveAsync(_db, request.Filter, cancellationToken);
        var sessionIds = scope.Sessions.Select(s => s.Id);

        var entered = await scope.Sessions.CountAsync(cancellationToken);

        var events = _db.BookingSessionEvents.AsNoTracking().Where(e => sessionIds.Contains(e.SessionId));

        var dateSelected = await CountDistinctSessionsAsync(events, e => e.EventType == BookingEventType.DateSelected, cancellationToken);
        var timeSelected = await CountDistinctSessionsAsync(events, e => e.EventType == BookingEventType.TimeSelected, cancellationToken);
        // "Details entered" = they typed something into the contact fields. Email or
        // name specifically, not any FieldChanged, so a stray phone/message keystroke
        // doesn't count as having filled the form in.
        var detailsEntered = await CountDistinctSessionsAsync(
            events,
            e => e.EventType == BookingEventType.FieldChanged
                && (e.FieldName == BookingFieldNames.Email || e.FieldName == BookingFieldNames.Name),
            cancellationToken);
        var confirmed = await CountDistinctSessionsAsync(events, e => e.EventType == BookingEventType.BookingSubmitted, cancellationToken);

        var counts = new[] { entered, dateSelected, timeSelected, detailsEntered, confirmed };
        var steps = new List<FunnelStepDto>(StepOrder.Length);
        for (var i = 0; i < StepOrder.Length; i++)
        {
            var previous = i == 0 ? counts[0] : counts[i - 1];
            steps.Add(new FunnelStepDto(
                StepOrder[i],
                counts[i],
                entered == 0 ? 0 : (double)counts[i] / entered,
                previous == 0 ? 0 : (double)counts[i] / previous));
        }

        var abandonment = await BuildAbandonmentAsync(scope, entered, cancellationToken);

        return new ConversionFunnelDto(steps, entered == 0 ? 0 : (double)confirmed / entered, abandonment);
    }

    private static Task<int> CountDistinctSessionsAsync(
        IQueryable<Domain.Entities.BookingSessionEvent> events,
        System.Linq.Expressions.Expression<Func<Domain.Entities.BookingSessionEvent, bool>> predicate,
        CancellationToken cancellationToken) =>
        events.Where(predicate).Select(e => e.SessionId).Distinct().CountAsync(cancellationToken);

    /// <summary>
    /// How far abandoned sessions got. Read from the session projection rather than
    /// replaying events: the projection already holds exactly "the furthest state
    /// this session reached" for these fields (SelectDate/ChangeField only ever set
    /// values, never clear them), so replaying the log would cost more and answer
    /// the same question.
    /// </summary>
    private static async Task<AbandonmentDto> BuildAbandonmentAsync(
        AnalyticsScope scope, int totalSessions, CancellationToken cancellationToken)
    {
        var abandoned = await scope.Sessions
            .Where(s => s.Status == BookingSessionStatus.Abandoned)
            .Select(s => new
            {
                HasDate = s.SelectedDate != null,
                HasTime = s.SelectedTime != null,
                HasDetails = s.Email != null || s.Name != null,
            })
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            return new AbandonmentDto(0, totalSessions == 0 ? 0 : 0, null, null, []);
        }

        // Furthest step index on the same ladder the funnel uses (1-based).
        var indices = abandoned
            .Select(a => a.HasDetails && a.HasTime ? 4 : a.HasTime ? 3 : a.HasDate ? 2 : 1)
            .ToList();

        var byStep = indices
            .GroupBy(i => i)
            .Select(g => new AbandonmentStepDto(StepOrder[g.Key - 1], g.Count(), (double)g.Count() / abandoned.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

        return new AbandonmentDto(
            abandoned.Count,
            totalSessions == 0 ? 0 : (double)abandoned.Count / totalSessions,
            indices.Average(),
            byStep.First().Step,
            byStep);
    }
}
