using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Bookings.Commands.ResendConfirmation;

public class ResendConfirmationCommandHandler : IRequestHandler<ResendConfirmationCommand>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IEmailNotificationService _emailNotificationService;

    public ResendConfirmationCommandHandler(IBookingTrackerDbContext db, IEmailNotificationService emailNotificationService)
    {
        _db = db;
        _emailNotificationService = emailNotificationService;
    }

    public async Task Handle(ResendConfirmationCommand request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, session.BookingPageId, request.RequestingOrganizerId, cancellationToken);

        if (session.PublicToken is null || session.Email is null || session.Name is null || session.SelectedDate is null || session.SelectedTime is null)
            throw new DomainException("This session has no confirmed booking to resend.");

        await _emailNotificationService.QueueResendBookingConfirmationAsync(session, cancellationToken);
    }
}
