using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTracker.Infrastructure.Persistence.Configurations;

public class BookingPageConfiguration : IEntityTypeConfiguration<BookingPage>
{
    public void Configure(EntityTypeBuilder<BookingPage> builder)
    {
        builder.ToTable("BookingPages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DurationMinutes).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.MinNoticeMinutes);
        builder.Property(x => x.MaxBookingWindowDays);
        builder.Property(x => x.MaxBookingsPerDay);

        // Non-nullable with a 0 (None) default, so every existing page keeps
        // behaving exactly as it did - no backfill, no meeting requested.
        builder.Property(x => x.MeetingProvider).HasConversion<byte>().IsRequired().HasDefaultValue(MeetingProviderType.None);

        builder.OwnsMany(x => x.Questions, question =>
        {
            question.ToTable("BookingQuestions");
            question.WithOwner().HasForeignKey(q => q.BookingPageId);
            question.HasKey(q => q.Id);
            // The domain assigns Id itself (Guid.NewGuid()) - without this, EF Core's
            // default "value generated on add" convention for Guid keys can misjudge a
            // freshly-added item inside an already-tracked owner's collection as an
            // existing row to UPDATE instead of a new one to INSERT, since the key is
            // already non-empty by the time DetectChanges sees it.
            question.Property(q => q.Id).ValueGeneratedNever();

            question.Property(q => q.Prompt).HasMaxLength(300).IsRequired();
            question.Property(q => q.DisplayOrder).IsRequired();
            question.HasIndex(q => new { q.BookingPageId, q.DisplayOrder });
        });
        builder.Navigation(x => x.Questions)
            .HasField("_questions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // The answerable counterpart to Questions above (which are read-only
        // instructions despite the entity name - see BookingQuestion). Same
        // owned-collection shape, including the ValueGeneratedNever needed for
        // domain-assigned Guid keys.
        builder.OwnsMany(x => x.FormFields, field =>
        {
            field.ToTable("BookingFormFields");
            field.WithOwner().HasForeignKey(f => f.BookingPageId);
            field.HasKey(f => f.Id);
            field.Property(f => f.Id).ValueGeneratedNever();

            field.Property(f => f.Label).HasMaxLength(BookingFieldLimits.CustomFieldLabelMaxLength).IsRequired();
            field.Property(f => f.Type).HasConversion<byte>().IsRequired();
            field.Property(f => f.IsRequired).IsRequired();
            field.Property(f => f.DisplayOrder).IsRequired();
            field.HasIndex(f => new { f.BookingPageId, f.DisplayOrder });
        });
        builder.Navigation(x => x.FormFields)
            .HasField("_formFields")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Aggregates reference each other by id only (no navigation properties),
        // but the FK constraint is still declared here for referential integrity.
        builder.HasOne<Organizer>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
