using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

/// <summary>
/// The append-only event log - the actual audit trail and source of truth.
/// Rows here are never updated or deleted by application code.
/// </summary>
public class BookingSessionEventConfiguration : IEntityTypeConfiguration<BookingSessionEvent>
{
    public void Configure(EntityTypeBuilder<BookingSessionEvent> builder)
    {
        builder.ToTable("BookingSessionEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.EventType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(BookingFieldLimits.EventFieldNameMaxLength);
        builder.Property(x => x.OldValue);
        builder.Property(x => x.NewValue);
        builder.Property(x => x.ClientSequenceNumber).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();

        builder.OwnsOne(x => x.Context, ctx =>
        {
            ctx.Property(c => c.IpAddress).HasColumnName("ClientIp").HasMaxLength(BookingFieldLimits.ClientIpMaxLength);
            ctx.Property(c => c.UserAgent).HasColumnName("UserAgent").HasMaxLength(BookingFieldLimits.UserAgentMaxLength);
        });

        // Definitive ordering for timeline reconstruction: by the client's own
        // monotonic counter first, DB identity only as a tiebreaker.
        builder.HasIndex(x => new { x.SessionId, x.ClientSequenceNumber });
        builder.HasIndex(x => new { x.BookingPageId, x.Timestamp });

        builder.HasOne<BookingSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not Cascade) to avoid a second cascade path into this table
        // alongside BookingPage -> BookingSession -> BookingSessionEvent.
        builder.HasOne<BookingPage>()
            .WithMany()
            .HasForeignKey(x => x.BookingPageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
