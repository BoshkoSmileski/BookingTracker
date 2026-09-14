using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Auth.Commands.Logout;

/// <summary>Idempotent: revoking an already-revoked or unknown token is not an error.</summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IBookingTrackerDbContext _db;

    public LogoutCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);
        if (token is null || !token.IsActive) return;

        token.Revoke();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
