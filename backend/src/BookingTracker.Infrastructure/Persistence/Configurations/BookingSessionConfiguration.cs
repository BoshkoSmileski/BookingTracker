using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

/// <summary>
/// The current-state projection table. Everything here is derivable from
/// BookingSessionEvents (see BookingSession.Rebuild) - this table exists
/// purely so the organizer dashboard doesn't have to replay events on read.
/// </summary>
public class BookingSessionConfiguration : IEntityTypeConfiguration<BookingSession>
{
    public void Configure(EntityTypeBuilder<BookingSession> builder)
    {
        builder.ToTable("BookingSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();

        builder.Property(x => x.Name).HasMaxLength(BookingFieldLimits.NameMaxLength);
        builder.Property(x => x.Email).HasMaxLength(BookingFieldLimits.EmailMaxLength);
        builder.Property(x => x.Phone).HasMaxLength(BookingFieldLimits.PhoneMaxLength);
        builder.Property(x => x.Message).HasMaxLength(BookingFieldLimits.MessageMaxLength);

        builder.Property(x => x.SelectedDate).HasColumnType("date");
        builder.Property(x => x.SelectedTime).HasColumnType("time");

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastActivityAt).IsRequired();
        builder.Property(x => x.LastClientSequenceNumber).IsRequired();

        builder.Property(x => x.BookingReference).HasMaxLength(20);
        builder.Property(x => x.PublicToken).HasMaxLength(64);
        builder.HasIndex(x => x.PublicToken).IsUnique().HasFilter("[PublicToken] IS NOT NULL");
        builder.HasIndex(x => x.BookingReference).IsUnique().HasFilter("[BookingReference] IS NOT NULL");

        // The booking's own online meeting. Nullable because most bookings have
        // none, and snapshotted from the page rather than joined to it - see
        // BookingSession.MeetingProvider. Both are written only by
        // BookingSession.Apply, so they are part of the projection like every
        // other column here and Rebuild() reconstructs them from the log.
        builder.Property(x => x.MeetingProvider).HasConversion<byte?>();
        builder.Property(x => x.MeetingUrl).HasMaxLength(BookingFieldLimits.MeetingUrlMaxLength);

        builder.Property(x => x.CancelledBy).HasConversion<byte?>();
        builder.Property(x => x.CancellationReason).HasMaxLength(BookingFieldLimits.CancellationReasonMaxLength);
        builder.Property(x => x.RescheduleCount).IsRequired();

        // Answers to the page's custom fields. Part of the projection, so it is
        // rebuilt from the event log like every other column here - see
        // BookingSession.ApplyAnswer. The unique index is what makes "one answer
        // per field" a schema guarantee rather than an upsert convention.
        builder.OwnsMany(x => x.Answers, answer =>
        {
            answer.ToTable("BookingSessionAnswers");
            answer.WithOwner().HasForeignKey(a => a.BookingSessionId);
            answer.HasKey(a => a.Id);
            answer.Property(a => a.Id).ValueGeneratedNever();

            answer.Property(a => a.BookingFormFieldId).IsRequired();
            answer.Property(a => a.Value).HasMaxLength(BookingFieldLimits.CustomAnswerMaxLength).IsRequired();
            answer.HasIndex(a => new { a.BookingSessionId, a.BookingFormFieldId }).IsUnique();
        });
        builder.Navigation(x => x.Answers)
            .HasField("_answers")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(x => x.LastContext, ctx =>
        {
            ctx.Property(c => c.IpAddress).HasColumnName("LastClientIp").HasMaxLength(BookingFieldLimits.ClientIpMaxLength);
            ctx.Property(c => c.UserAgent).HasColumnName("LastUserAgent").HasMaxLength(BookingFieldLimits.UserAgentMaxLength);
        });

        // The index the booking conflict check runs on, and the reason it has
        // the shape it does is lock footprint rather than read speed.
        //
        // BookingConflictChecker asks "which of THIS ORGANIZER's bookings are on
        // this date", inside a Serializable transaction. BookingSessions has no
        // OrganizerId column - the rule is organizer-wide, so the query reaches
        // it by joining through BookingPages - which leaves the leading columns
        // available to SQL Server as (BookingPageId, Status, SelectedDate).
        //
        // Before SelectedDate and SelectedTime were part of this index, no single
        // index could answer the query: (Status, SelectedDate, SelectedTime)
        // matched the filter but not the join column, and this one matched the
        // join column but not the filter. SQL Server therefore combined the two,
        // and the second half of that combination was a FULL index scan - which
        // under Serializable is not expressible as key-range locks, so it took a
        // table-level S lock on BookingSessions instead. Every booking submission
        // in the system locked the whole table, so any two concurrent bookings
        // deadlocked even when they had nothing to do with each other. Measured:
        // sixteen guests booking sixteen DIFFERENT organizers produced one
        // booking and fifteen HTTP 500s, in every round.
        //
        // Seeking per booking page instead confines the range lock to one page's
        // bookings on one date, so different organizers lock disjoint key ranges
        // and never meet. SelectedTime is INCLUDEd rather than keyed because the
        // query only projects it.
        //
        // BookingPageId stays the leading column, so this also remains the FK
        // index it already was, and every (BookingPageId, Status) seek that used
        // the narrower version still seeks.
        builder.HasIndex(x => new { x.BookingPageId, x.Status, x.SelectedDate })
            .IncludeProperties(x => x.SelectedTime);

        builder.HasIndex(x => new { x.Status, x.LastActivityAt });
        builder.HasIndex(x => new { x.Status, x.SelectedDate, x.SelectedTime });

        builder.HasOne<BookingPage>()
            .WithMany()
            .HasForeignKey(x => x.BookingPageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
