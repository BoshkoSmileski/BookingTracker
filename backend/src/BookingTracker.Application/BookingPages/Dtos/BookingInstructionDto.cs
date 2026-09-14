namespace BookingTracker.Application.BookingPages.Dtos;

/// <summary>
/// One line of guidance an organizer wants visitors to read before booking, e.g.
/// "Please have your account number ready".
///
/// Deliberately NOT a question: there is no answer field and no visitor input.
/// The Domain entity behind it is still called BookingQuestion (renaming it
/// would mean a migration for no behavioural gain), but every name a caller or
/// an organizer sees says "instruction" - see AddBookingInstructionCommand.
/// </summary>
public record BookingInstructionDto(Guid Id, string Text, int DisplayOrder);
