using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class BookingReminderConfiguration : IEntityTypeConfiguration<BookingReminder>
{
    public void Configure(EntityTypeBuilder<BookingReminder> builder)
    {
        builder.ToTable("BookingReminders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MinutesBeforeEvent).IsRequired();
        builder.Property(x => x.ScheduledForUtc).IsRequired();
        builder.Property(x => x.MeetingStartsAtUtc).IsRequired();
        builder.Property(x => x.Channel).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ResolutionReason).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        // THE duplicate-delivery guarantee. Filtered to Scheduled (0) only, so the
        // database physically cannot hold two pending reminders for the same booking
        // and lead time - no matter how often the sweeper runs, how many times the
        // app restarts mid-sweep, or how many times reminders are regenerated.
        // Terminal rows (Queued/Cancelled/Skipped) are excluded on purpose: after a
        // reschedule a booking legitimately has an already-sent 24h reminder for the
        // old time AND a fresh scheduled one for the new time.
        builder.HasIndex(x => new { x.BookingSessionId, x.MinutesBeforeEvent })
            .IsUnique()
            .HasFilter($"[Status] = {(byte)BookingReminderStatus.Scheduled}")
            .HasDatabaseName("UX_BookingReminders_Session_Offset_Scheduled");

        // The sweeper's only query: due, still-scheduled work, oldest first.
        builder.HasIndex(x => new { x.Status, x.ScheduledForUtc });

        builder.HasOne<BookingSession>()
            .WithMany()
            .HasForeignKey(x => x.BookingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: BookingPage -> BookingSession -> BookingReminder is
        // already a cascade path, and SQL Server rejects a second one into the same
        // table (same reasoning as BookingSessionEventConfiguration).
        builder.HasOne<BookingPage>()
            .WithMany()
            .HasForeignKey(x => x.BookingPageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Intentionally no FK to EmailNotification: the link is a soft reference to a
        // delivery record whose lifetime is not owned by the reminder, and a cascade
        // from notification cleanup must never delete reminder history.
        builder.Property(x => x.EmailNotificationId);
        builder.HasIndex(x => x.EmailNotificationId);
    }
}
