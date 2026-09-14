using BookingTracker.Application.Auth.Common;
using BookingTracker.Application.Auth.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = BookingTracker.Application.Common.Exceptions.ValidationException;

namespace BookingTracker.Application.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResultDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RegisterCommandHandler(IBookingTrackerDbContext db, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var emailTaken = await _db.Organizers.AnyAsync(o => o.Email == request.Email, cancellationToken);
        if (emailTaken)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(request.Email), "An organizer with this email already exists.")]);
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var organizer = Organizer.Register(request.Name, request.Email, passwordHash);
        _db.Organizers.Add(organizer);

        var (refreshTokenEntity, result) = AuthResultFactory.Build(_jwtTokenGenerator, organizer);
        _db.RefreshTokens.Add(refreshTokenEntity);

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
