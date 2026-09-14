using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetGoogleAuthorizationUrl;

public class GetGoogleAuthorizationUrlQueryHandler : IRequestHandler<GetGoogleAuthorizationUrlQuery, string>
{
    private readonly IEnumerable<ICalendarProvider> _providers;

    public GetGoogleAuthorizationUrlQueryHandler(IEnumerable<ICalendarProvider> providers) => _providers = providers;

    public Task<string> Handle(GetGoogleAuthorizationUrlQuery request, CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderType == CalendarProviderType.Google)
            ?? throw new InvalidOperationException("No ICalendarProvider registered for Google.");

        return Task.FromResult(provider.BuildAuthorizationUrl(request.State));
    }
}
