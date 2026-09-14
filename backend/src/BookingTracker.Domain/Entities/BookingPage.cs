using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

public class BookingPage : Entity<Guid>
{
    /// <summary>
    /// A booking form is a step in a wizard, not a survey - past this many
    /// fields the details step stops being something a visitor completes.
    /// Answers already given for a *removed* field are deliberately kept (see
    /// BookingSessionAnswer): they are historical fact about a booking made
    /// while the field existed, and deleting them would rewrite a projection
    /// the event log still describes.
    /// </summary>
    public const int MaxFormFields = 10;

    private readonly List<BookingQuestion> _questions = [];
    private readonly List<BookingFormField> _formFields = [];

    public Guid OrganizerId { get; private set; }
    public string Slug { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public int DurationMinutes { get; private set; }
    public int BufferBeforeMinutes { get; private set; }
    public int BufferAfterMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Minutes of advance notice required before a slot can be booked. Null = no minimum.</summary>
    public int? MinNoticeMinutes { get; private set; }

    /// <summary>How many days into the future this page can be booked. Null = no cap.</summary>
    public int? MaxBookingWindowDays { get; private set; }

    /// <summary>Maximum number of confirmed bookings allowed on any single day. Null = unlimited.</summary>
    public int? MaxBookingsPerDay { get; private set; }

    /// <summary>
    /// How bookings on this page meet. Non-nullable with a None default, so
    /// every existing page keeps behaving exactly as it did (no meeting link,
    /// no conference requested) without a backfill.
    ///
    /// This is the *current* configuration, and is deliberately not what an
    /// already-made booking reads: BookingSession snapshots its own provider
    /// when its meeting is created, so switching a page to In person tomorrow
    /// never invalidates the Meet link a guest is already holding.
    /// </summary>
    public MeetingProviderType MeetingProvider { get; private set; }

    public IReadOnlyList<BookingQuestion> Questions => _questions.AsReadOnly();

    /// <summary>Organizer-defined fields visitors answer while booking. Distinct from <see cref="Questions"/>, which are read-only instructions.</summary>
    public IReadOnlyList<BookingFormField> FormFields => _formFields.AsReadOnly();

    private BookingPage() { }

    public static BookingPage Create(
        Guid organizerId, string slug, string title, int durationMinutes,
        int bufferBeforeMinutes = 0, int bufferAfterMinutes = 0, string? description = null,
        int? minNoticeMinutes = null, int? maxBookingWindowDays = null, int? maxBookingsPerDay = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));
        if (durationMinutes <= 0)
            throw new ArgumentException("Duration must be positive.", nameof(durationMinutes));
        ValidateBuffers(bufferBeforeMinutes, bufferAfterMinutes);
        ValidateLimits(minNoticeMinutes, maxBookingWindowDays, maxBookingsPerDay);

        return new BookingPage
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Slug = slug,
            Title = title,
            Description = description,
            DurationMinutes = durationMinutes,
            BufferBeforeMinutes = bufferBeforeMinutes,
            BufferAfterMinutes = bufferAfterMinutes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            MinNoticeMinutes = minNoticeMinutes,
            MaxBookingWindowDays = maxBookingWindowDays,
            MaxBookingsPerDay = maxBookingsPerDay,
            MeetingProvider = MeetingProviderType.None
        };
    }

    /// <summary>
    /// Changes how future bookings on this page meet. Its own intent method
    /// (and its own command) rather than an extra parameter on UpdateDetails,
    /// for the same reason calendar event settings got their own command
    /// instead of being folded into sync settings - these are edited on
    /// different screens, at different times, for different reasons.
    ///
    /// Bookings that already exist are untouched: their meeting - or absence
    /// of one - was settled when they were confirmed.
    /// </summary>
    public void UpdateMeetingSettings(MeetingProviderType meetingProvider)
    {
        if (!Enum.IsDefined(meetingProvider))
            throw new DomainException($"Unknown meeting provider '{meetingProvider}'.");

        MeetingProvider = meetingProvider;
    }

    /// <summary>Changing scheduling settings never affects bookings already Submitted - only future slot generation.</summary>
    public void UpdateSchedulingSettings(int durationMinutes, int bufferBeforeMinutes, int bufferAfterMinutes)
    {
        if (durationMinutes <= 0)
            throw new DomainException("Duration must be positive.");
        ValidateBuffers(bufferBeforeMinutes, bufferAfterMinutes);

        DurationMinutes = durationMinutes;
        BufferBeforeMinutes = bufferBeforeMinutes;
        BufferAfterMinutes = bufferAfterMinutes;
    }

    public void UpdateDetails(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Title is required.");

        Title = title;
        Description = description;
    }

    public void UpdateLimits(int? minNoticeMinutes, int? maxBookingWindowDays, int? maxBookingsPerDay)
    {
        ValidateLimits(minNoticeMinutes, maxBookingWindowDays, maxBookingsPerDay);

        MinNoticeMinutes = minNoticeMinutes;
        MaxBookingWindowDays = maxBookingWindowDays;
        MaxBookingsPerDay = maxBookingsPerDay;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public BookingQuestion AddQuestion(string prompt, int displayOrder)
    {
        var question = BookingQuestion.Create(Id, prompt, displayOrder);
        _questions.Add(question);
        return question;
    }

    public void RemoveQuestion(Guid questionId)
    {
        var question = _questions.FirstOrDefault(q => q.Id == questionId)
            ?? throw new DomainException($"Instruction {questionId} does not belong to this booking page.");
        _questions.Remove(question);
    }

    public BookingFormField AddFormField(string label, BookingFieldType type, bool isRequired)
    {
        if (_formFields.Count >= MaxFormFields)
            throw new DomainException($"A booking page cannot have more than {MaxFormFields} custom fields.");

        // DisplayOrder is derived from the current tail rather than taken from
        // the caller: it is the only ordering the visitor and the organizer both
        // see, so letting two callers pick the same number is a defect waiting
        // to happen. Removing a field leaves a gap, which is harmless - the
        // order is a sort key, never an index.
        var nextOrder = _formFields.Count == 0 ? 0 : _formFields.Max(f => f.DisplayOrder) + 1;
        var field = BookingFormField.Create(Id, label, type, isRequired, nextOrder);
        _formFields.Add(field);
        return field;
    }

    public void RemoveFormField(Guid fieldId)
    {
        var field = _formFields.FirstOrDefault(f => f.Id == fieldId)
            ?? throw new DomainException($"Field {fieldId} does not belong to this booking page.");
        _formFields.Remove(field);
    }

    private static void ValidateBuffers(int bufferBeforeMinutes, int bufferAfterMinutes)
    {
        if (bufferBeforeMinutes < 0 || bufferAfterMinutes < 0)
            throw new DomainException("Buffer minutes cannot be negative.");
    }

    private static void ValidateLimits(int? minNoticeMinutes, int? maxBookingWindowDays, int? maxBookingsPerDay)
    {
        if (minNoticeMinutes is < 0)
            throw new DomainException("Minimum notice cannot be negative.");
        if (maxBookingWindowDays is < 1)
            throw new DomainException("Maximum booking window must be at least 1 day.");
        if (maxBookingsPerDay is < 1)
            throw new DomainException("Maximum bookings per day must be at least 1.");
    }
}
