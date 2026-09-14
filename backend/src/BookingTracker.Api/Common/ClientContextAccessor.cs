namespace BookingTracker.Api.Common;

public static class ClientContextAccessor
{
    public static (string? Ip, string? UserAgent) Read(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        return (ip, string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }
}
