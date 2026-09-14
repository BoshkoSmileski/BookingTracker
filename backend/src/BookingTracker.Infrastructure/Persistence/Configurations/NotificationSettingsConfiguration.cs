using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        builder.ToTable("NotificationSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotifyGuestOnBooking).IsRequired();
        builder.Property(x => x.NotifyOrganizerOnBooking).IsRequired();
        builder.Property(x => x.RemindersEnabled).IsRequired();
        builder.Property(x => x.ReminderMinutesBeforeEventCsv).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        // One notification settings row per organizer, same shape as WorkingSchedule/CalendarConnection.
        builder.HasIndex(x => x.OrganizerId).IsUnique();

        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.ReminderMinutesBeforeEvent);
    }
}
