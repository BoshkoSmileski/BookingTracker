using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class WorkingScheduleConfiguration : IEntityTypeConfiguration<WorkingSchedule>
{
    public void Configure(EntityTypeBuilder<WorkingSchedule> builder)
    {
        builder.ToTable("WorkingSchedules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // One schedule per organizer.
        builder.HasIndex(x => x.OrganizerId).IsUnique();

        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
