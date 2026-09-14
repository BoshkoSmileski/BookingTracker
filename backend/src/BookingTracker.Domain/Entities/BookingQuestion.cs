using BookingTracker.Domain.Common;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One line of organizer-authored guidance shown to visitors on a booking page
/// (e.g. "Please have your account number ready"). Visitors read it; they never
/// answer it - the BookingSession event-sourced model has a fixed field set
/// (Name/Email/Phone/Message) and extending it to collect arbitrary per-item
/// answers is a separate feature.
///
/// The type and its table keep the original "Question" name: everything the
/// Application layer, the API and the UI expose calls this a **booking
/// instruction**, which is what it is, and renaming the entity would mean a
/// migration and a snapshot churn for no behavioural gain. The translation
/// happens once, at BookingPageMappings.
/// </summary>
public class BookingQuestion : Entity<Guid>
{
    public Guid BookingPageId { get; private set; }
    public string Prompt { get; private set; } = default!;
    public int DisplayOrder { get; private set; }

    private BookingQuestion() { }

    public static BookingQuestion Create(Guid bookingPageId, string prompt, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new DomainException("Instruction text is required.");

        return new BookingQuestion
        {
            Id = Guid.NewGuid(),
            BookingPageId = bookingPageId,
            Prompt = prompt,
            DisplayOrder = displayOrder
        };
    }
}
