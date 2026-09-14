using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class AvailabilityOverrideConfiguration : IEntityTypeConfiguration<AvailabilityOverride>
{
    public void Configure(EntityTypeBuilder<AvailabilityOverride> builder)
    {
        builder.ToTable("AvailabilityOverrides");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Date).HasColumnType("date").IsRequired();
        builder.Property(x => x.Note).HasMaxLength(200);
        builder.Property(x => x.CreatedAt).IsRequired();

        // One override per organizer per date, enforced by the database rather
        // than by application care. "The hours for 15 August" is a single fact;
        // two rows for one date would need a tie-break rule that no organizer
        // could predict, and SlotGenerationService.ResolveOpenRanges takes the
        // first match - which would make the answer depend on row order.
        // Doubles as the lookup index for both the organizer's list and the
        // per-window load in GetAvailableSlotsQueryHandler.
        builder.HasIndex(x => new { x.OrganizerId, x.Date }).IsUnique();

        // The same owned-collection shape as WorkingDay.Intervals - a day's open
        // hours are a day's open hours, whether the day is a weekday template or
        // one specific date, so the storage is the same rather than a second
        // spelling of it.
        builder.OwnsMany(x => x.Ranges, range =>
        {
            range.ToTable("AvailabilityOverrideRanges");
            range.WithOwner().HasForeignKey("AvailabilityOverrideId");
            range.Property(r => r.Start).HasColumnType("time").IsRequired();
            range.Property(r => r.End).HasColumnType("time").IsRequired();
        });
        builder.Navigation(x => x.Ranges)
            .HasField("_ranges")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
