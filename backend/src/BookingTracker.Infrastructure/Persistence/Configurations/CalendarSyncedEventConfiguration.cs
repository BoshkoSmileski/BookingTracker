using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class CalendarSyncedEventConfiguration : IEntityTypeConfiguration<CalendarSyncedEvent>
{
    public void Configure(EntityTypeBuilder<CalendarSyncedEvent> builder)
    {
        builder.ToTable("CalendarSyncedEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalEventId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // One tracked external event per booking session.
        builder.HasIndex(x => x.BookingSessionId).IsUnique();

        builder.HasOne<BookingSession>()
            .WithMany()
            .HasForeignKey(x => x.BookingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CalendarConnection>()
            .WithMany()
            .HasForeignKey(x => x.CalendarConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
