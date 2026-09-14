using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Auth;

namespace BookingTracker.Api.Common;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? OrganizerId => _httpContextAccessor.HttpContext?.User.GetOrganizerId();
}
