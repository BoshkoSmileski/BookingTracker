using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Commands.SaveAvailabilityOverride;

/// <summary>
/// Sets the opening hours for one date, creating the override or replacing the
/// existing one for that date.
///
/// ONE upsert command rather than separate Create and Update commands. That is a
/// departure from the Create/Delete pair AvailabilityException uses, and it is
/// deliberate: at most one override can exist per date, so "add hours for 15
/// August" and "change the hours for 15 August" are the same request with the
/// same body, and splitting them would make the client responsible for knowing
/// which one to send - and for handling the 409 when it guessed wrong. The
/// weekly schedule already establishes this shape (SaveWorkingScheduleCommand is
/// an idempotent upsert of the whole week); this is the same idea for one day.
/// </summary>
/// <param name="Ranges">The day's open hours. Empty closes the day.</param>
public record SaveAvailabilityOverrideCommand(
    Guid OrganizerId,
    DateOnly Date,
    IReadOnlyList<TimeRangeDto> Ranges,
    string? Note) : IRequest<AvailabilityOverrideDto>;
