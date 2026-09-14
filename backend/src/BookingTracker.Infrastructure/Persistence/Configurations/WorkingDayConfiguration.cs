using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class WorkingDayConfiguration : IEntityTypeConfiguration<WorkingDay>
{
    public void Configure(EntityTypeBuilder<WorkingDay> builder)
    {
        builder.ToTable("WorkingDays");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayOfWeek).HasConversion<byte>().IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();

        // Belt-and-braces: SaveWorkingScheduleCommandHandler always replaces a
        // schedule's days wholesale, so this should never trip in practice.
        builder.HasIndex(x => new { x.WorkingScheduleId, x.DayOfWeek }).IsUnique();

        builder.OwnsMany(x => x.Intervals, interval =>
        {
            interval.ToTable("WorkingDayIntervals");
            interval.WithOwner().HasForeignKey("WorkingDayId");
            interval.Property<int>("Id");
            interval.HasKey("WorkingDayId", "Id");

            interval.Property(i => i.Start).HasColumnName("StartTime").HasColumnType("time").IsRequired();
            interval.Property(i => i.End).HasColumnName("EndTime").HasColumnType("time").IsRequired();
        });
        builder.Navigation(x => x.Intervals)
            .HasField("_intervals")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<WorkingSchedule>()
            .WithMany()
            .HasForeignKey(x => x.WorkingScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
