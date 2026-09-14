using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetGoogleAuthorizationUrl;

/// <summary>State is built by the Api layer (it needs IDataProtectionProvider, a framework concern) and just passed through here unchanged.</summary>
public record GetGoogleAuthorizationUrlQuery(string State) : IRequest<string>;
