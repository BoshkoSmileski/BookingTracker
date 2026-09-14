using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One organizer-defined question a visitor answers while booking - "Company",
/// "What would you like to discuss?", "Project name". The organizer defines the
/// field; the visitor supplies a <see cref="BookingSessionAnswer"/> for it.
///
/// Deliberately NOT the same thing as <see cref="BookingQuestion"/>, despite
/// that type's name: a BookingQuestion is a read-only *instruction* a visitor
/// never replies to (the name is historical - see its own doc comment). This is
/// the answerable one. Nothing above the Domain layer uses the word "question"
/// for either, so the two can never be confused by an identifier alone.
///
/// Answers are captured as ordinary FieldChanged events keyed by
/// BookingFieldNames.ForCustomField(Id), which is why adding this feature needed
/// no new BookingEventType and left BookingSession.Rebuild working unchanged.
/// </summary>
public class BookingFormField : Entity<Guid>
{
    public Guid BookingPageId { get; private set; }
    public string Label { get; private set; } = default!;
    public BookingFieldType Type { get; private set; }

    /// <summary>When true, a session cannot be submitted without a non-blank answer. Enforced at submit time, not as a session invariant - see SubmitBookingSessionCommandHandler.</summary>
    public bool IsRequired { get; private set; }

    public int DisplayOrder { get; private set; }

    private BookingFormField() { }

    public static BookingFormField Create(Guid bookingPageId, string label, BookingFieldType type, bool isRequired, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException("A booking form field needs a label.");

        if (label.Length > BookingFieldLimits.CustomFieldLabelMaxLength)
            throw new DomainException($"A field label cannot exceed {BookingFieldLimits.CustomFieldLabelMaxLength} characters.");

        return new BookingFormField
        {
            Id = Guid.NewGuid(),
            BookingPageId = bookingPageId,
            Label = label.Trim(),
            Type = type,
            IsRequired = isRequired,
            DisplayOrder = displayOrder
        };
    }
}
