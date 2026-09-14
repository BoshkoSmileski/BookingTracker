using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

/// <summary>The persisted email queue - see EmailNotification's doc comment. HtmlBody/TextBody are system-generated (never raw user input), so an unbounded nvarchar(max) is fine, unlike user-supplied fields (see BookingFieldLimits).</summary>
public class EmailNotificationConfiguration : IEntityTypeConfiguration<EmailNotification>
{
    public void Configure(EntityTypeBuilder<EmailNotification> builder)
    {
        builder.ToTable("EmailNotifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotificationType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ToEmail).HasMaxLength(BookingFieldLimits.EmailMaxLength).IsRequired();
        builder.Property(x => x.ToName).HasMaxLength(BookingFieldLimits.NameMaxLength).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        builder.Property(x => x.HtmlBody).IsRequired();
        builder.Property(x => x.TextBody).IsRequired();
        builder.Property(x => x.EventLogFieldName).HasMaxLength(BookingFieldLimits.EventFieldNameMaxLength).IsRequired();

        // The rendered RFC5545 invitation, stored with the email it belongs to. Like
        // the bodies, it is system-generated (never user input), so nvarchar(max) is
        // appropriate and BookingFieldLimits does not apply. Nullable throughout:
        // reminders carry no invitation - see IcsCalendarInvitation.
        builder.Property(x => x.IcsContent);
        builder.Property(x => x.IcsFileName).HasMaxLength(100);
        builder.Property(x => x.IcsMethod).HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.MaxAttempts).IsRequired();
        builder.Property(x => x.NextAttemptAtUtc).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        // The queue processor's core query: "give me due, pending work" - see EmailQueueProcessor.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });

        builder.HasOne<BookingSession>()
            .WithMany()
            .HasForeignKey(x => x.BookingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
