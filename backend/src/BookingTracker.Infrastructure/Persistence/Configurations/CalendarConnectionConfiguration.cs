using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class CalendarConnectionConfiguration : IEntityTypeConfiguration<CalendarConnection>
{
    public void Configure(EntityTypeBuilder<CalendarConnection> builder)
    {
        builder.ToTable("CalendarConnections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ExternalAccountEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.ExternalCalendarId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ExternalCalendarName).HasMaxLength(200).IsRequired();
        // Ciphertext produced by Data Protection - comfortably wider than a raw OAuth token.
        builder.Property(x => x.EncryptedAccessToken).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.EncryptedRefreshToken).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AccessTokenExpiresAtUtc).IsRequired();
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).IsRequired();

        // Explicit column name: this used to be called LastSyncedAtUtc - keeping the same
        // column via a rename (not a drop+add) preserves already-recorded sync history for
        // any organizer connected before this field was clarified into two (success/failure).
        builder.Property(x => x.LastSuccessfulSyncAtUtc).HasColumnName("LastSyncedAtUtc");

        builder.Property(x => x.EventTitleFormat).HasMaxLength(200).IsRequired().HasDefaultValue(CalendarConnection.DefaultEventTitleFormat);
        builder.Property(x => x.EventVisibility).HasMaxLength(20).IsRequired().HasDefaultValue(CalendarConnection.DefaultEventVisibility);
        builder.Property(x => x.AutoDeleteCancelledBookings).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.AutoUpdateRescheduledBookings).IsRequired().HasDefaultValue(true);

        // One calendar connection per organizer, same shape as WorkingSchedule.
        builder.HasIndex(x => x.OrganizerId).IsUnique();

        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
