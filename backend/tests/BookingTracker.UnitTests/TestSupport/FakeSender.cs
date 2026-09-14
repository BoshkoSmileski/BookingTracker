using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using MediatR;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Stands in for MediatR's <see cref="ISender"/> where a handler dispatches a
/// query of its own - today only <see cref="GetAvailableSlotsQuery"/>, which
/// <c>BookableSlotGuard</c> sends to find out whether the chosen slot is one the
/// page actually offers.
///
/// Hand-written rather than mocked, for the reason <see cref="FakeCalendarSyncService"/>
/// gives: exactly one member is exercised, and a mocking library would add a
/// dependency to express it. Anything other than that one query <b>throws</b>
/// rather than returning a default - a handler that starts dispatching something
/// new must fail loudly here rather than silently receive null.
///
/// The permissive default is deliberate and is what keeps these tests about
/// their own subject. A test of the required-answer rule, or of what a submit
/// queues, is not a test of availability; giving it a fake that reports the page
/// as open lets it seed a session without also seeding a working schedule, and
/// leaves availability to be pinned where it belongs - over HTTP, against the
/// real query, in <c>AnonymousBookingIntegrityTests</c>.
/// </summary>
public sealed class FakeSender : ISender
{
    private readonly bool _offerEverything;
    private readonly HashSet<(DateOnly Date, TimeOnly Time)> _offered;

    private FakeSender(bool offerEverything, HashSet<(DateOnly, TimeOnly)> offered)
    {
        _offerEverything = offerEverything;
        _offered = offered;
    }

    /// <summary>Reports every minute of the requested range as bookable.</summary>
    public static FakeSender OfferingAnySlot() => new(true, []);

    /// <summary>Reports only these slots as bookable, so a test can drive a refusal.</summary>
    public static FakeSender Offering(params (DateOnly Date, TimeOnly Time)[] slots) => new(false, [.. slots]);

    /// <summary>Every <see cref="GetAvailableSlotsQuery"/> this fake has answered.</summary>
    public List<GetAvailableSlotsQuery> SlotQueries { get; } = [];

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is not GetAvailableSlotsQuery query)
        {
            throw new NotSupportedException(
                $"{nameof(FakeSender)} only answers {nameof(GetAvailableSlotsQuery)}; got {request.GetType().Name}. " +
                "Extend this fake deliberately rather than letting a new dispatch go unnoticed.");
        }

        SlotQueries.Add(query);
        return Task.FromResult((TResponse)(object)(IReadOnlyList<AvailableSlotDto>)BuildSlots(query));
    }

    private List<AvailableSlotDto> BuildSlots(GetAvailableSlotsQuery query)
    {
        var slots = new List<AvailableSlotDto>();

        for (var date = query.FromDate; date <= query.ToDate; date = date.AddDays(1))
        {
            if (_offerEverything)
            {
                // Minute resolution rather than the real 15-minute grid: a fake
                // that claims to offer everything must not quietly refuse a test
                // whose chosen time happens to fall between the real steps.
                for (var minute = 0; minute < 24 * 60; minute++)
                {
                    slots.Add(Slot(date, new TimeOnly(minute / 60, minute % 60)));
                }
                continue;
            }

            slots.AddRange(_offered.Where(s => s.Date == date).Select(s => Slot(s.Date, s.Time)));
        }

        return slots;
    }

    private static AvailableSlotDto Slot(DateOnly date, TimeOnly time)
    {
        var startUtc = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
        return new AvailableSlotDto(date, time, time.AddMinutes(30), startUtc, startUtc.AddMinutes(30));
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
        => throw new NotSupportedException($"{nameof(FakeSender)} only answers {nameof(GetAvailableSlotsQuery)}.");

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(FakeSender)} does not support untyped Send.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(FakeSender)} does not support streaming.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(FakeSender)} does not support streaming.");
}
