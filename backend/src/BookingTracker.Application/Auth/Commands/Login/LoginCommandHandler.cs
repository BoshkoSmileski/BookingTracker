using BookingTracker.Application.Auth.Common;
using BookingTracker.Application.Auth.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResultDto>
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly IBookingTrackerDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(IBookingTrackerDbContext db, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var organizer = await _db.Organizers.FirstOrDefaultAsync(o => o.Email == request.Email, cancellationToken)
            ?? throw new AuthenticationException(InvalidCredentialsMessage);

        if (!_passwordHasher.VerifyPassword(organizer.PasswordHash, request.Password))
            throw new AuthenticationException(InvalidCredentialsMessage);

        var (refreshTokenEntity, result) = AuthResultFactory.Build(_jwtTokenGenerator, organizer);
        _db.RefreshTokens.Add(refreshTokenEntity);

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
