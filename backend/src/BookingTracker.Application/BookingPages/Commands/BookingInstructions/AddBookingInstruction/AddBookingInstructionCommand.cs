using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.BookingInstructions.AddBookingInstruction;

/// <summary>
/// Adds one line of guidance shown to visitors on the public booking page.
///
/// The Domain still calls this a BookingQuestion (entity and table), because
/// renaming those would cost a migration for no behavioural gain. Everything
/// from this layer outward - command, DTO, route, UI - says "instruction",
/// which is what it actually is: text a visitor reads, never answers.
/// </summary>
public record AddBookingInstructionCommand(Guid OrganizerId, Guid BookingPageId, string Text) : IRequest<BookingPageDetailDto>;
