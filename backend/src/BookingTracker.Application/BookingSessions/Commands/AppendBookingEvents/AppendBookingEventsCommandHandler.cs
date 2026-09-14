using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Commands.AppendBookingEvents;

/// <summary>
/// The main tracking endpoint's handler: applies a batch of client-observed
/// interactions to the session aggregate, one event at a time, in the order
/// they actually occurred on the client (ClientSequenceNumber) rather than
/// the order they happen to appear in the batch or arrive over the network.
/// </summary>
public class AppendBookingEventsCommandHandler : IRequestHandler<AppendBookingEventsCommand, BookingSessionDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IEventBroadcaster _broadcaster;

    public AppendBookingEventsCommandHandler(IBookingTrackerDbContext db, IEventBroadcaster broadcaster)
    {
        _db = db;
        _broadcaster = broadcaster;
    }

    public async Task<BookingSessionDto> Handle(AppendBookingEventsCommand request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        var context = new ClientContext(request.ClientIp, request.UserAgent);
        var appliedEvents = new List<BookingSessionEvent>(request.Events.Count);

        foreach (var dto in request.Events.OrderBy(e => e.ClientSequenceNumber))
        {
            appliedEvents.Add(Apply(session, dto, context));
        }

        _db.BookingSessionEvents.AddRange(appliedEvents);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var @event in appliedEvents)
        {
            await _broadcaster.BroadcastEventAsync(@event.ToDto(), cancellationToken);
        }
        await _broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);

        return session.ToDto();
    }

    private static BookingSessionEvent Apply(BookingSession session, ClientBookingEventDto dto, ClientContext context)
    {
        if (!Enum.TryParse<BookingEventType>(dto.EventType, ignoreCase: true, out var eventType))
            throw new DomainException($"Unknown event type '{dto.EventType}'.");

        return eventType switch
        {
            BookingEventType.FieldChanged => session.ChangeField(RequireFieldName(dto), dto.NewValue, dto.ClientSequenceNumber, context),
            BookingEventType.DateSelected => session.SelectDate(ParseDateOnly(dto.NewValue), dto.ClientSequenceNumber, context),
            BookingEventType.TimeSelected => session.SelectTime(ParseTimeOnly(dto.NewValue), dto.ClientSequenceNumber, context),
            BookingEventType.UserInactive => session.MarkInactive(dto.ClientSequenceNumber, context),
            BookingEventType.UserActive => session.MarkActive(dto.ClientSequenceNumber, context),
            BookingEventType.BrowserClosed => session.MarkBrowserClosed(dto.ClientSequenceNumber, context),
            _ => throw new DomainException(
                $"Event type '{dto.EventType}' cannot be reported through the batch endpoint; it is issued by a dedicated command.")
        };
    }

    private static string RequireFieldName(ClientBookingEventDto dto)
        => dto.FieldName ?? throw new DomainException("FieldName is required for FieldChanged events.");

    private static DateOnly? ParseDateOnly(string? value)
        => string.IsNullOrEmpty(value) ? null : DateOnly.Parse(value);

    private static TimeOnly? ParseTimeOnly(string? value)
        => string.IsNullOrEmpty(value) ? null : TimeOnly.Parse(value);
}
