using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class AvailabilityExceptionConfiguration : IEntityTypeConfiguration<AvailabilityException>
{
    public void Configure(EntityTypeBuilder<AvailabilityException> builder)
    {
        builder.ToTable("AvailabilityExceptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Date).HasColumnType("date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.StartTime).HasColumnType("time");
        builder.Property(x => x.EndTime).HasColumnType("time");
        builder.Property(x => x.Type).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired();

        // Availability reads ask "which of this organizer's exceptions overlap
        // [from..to]", which is a predicate on both ends of the range.
        builder.HasIndex(x => new { x.OrganizerId, x.Date, x.EndDate });

        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
