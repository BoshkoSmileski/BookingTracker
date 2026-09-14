using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.BookingInstructions.RemoveBookingInstruction;

public record RemoveBookingInstructionCommand(Guid OrganizerId, Guid BookingPageId, Guid InstructionId) : IRequest<BookingPageDetailDto>;
