using BookingTracker.Domain.Enums;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// How many rows each predicate of the conflict query eliminates, measured
/// rather than assumed.
///
/// This is what decides whether an index question is worth asking at all. Under
/// Serializable the shared key-range lock covers whatever the chosen access path
/// had to READ, not what the query returned - so the gap between
/// <see cref="Counts.SubmittedOnDate"/> (everything an index led by Status and
/// SelectedDate matches) and <see cref="Counts.SubmittedOnDateForOrganizer"/>
/// (what the caller actually cares about) is the size of the potential problem.
/// If those two numbers are the same, no index can narrow anything.
/// </summary>
public static class ConflictRowCounts
{
    public sealed record Counts(int Total, int Submitted, int SubmittedOnDate, int SubmittedOnDateForOrganizer);

    public static async Task<Counts> MeasureAsync(ConflictDataset.Shape shape)
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();

        var total = await db.BookingSessions.CountAsync();

        var submitted = await db.BookingSessions
            .CountAsync(s => s.Status == BookingSessionStatus.Submitted);

        var submittedOnDate = await db.BookingSessions
            .CountAsync(s => s.Status == BookingSessionStatus.Submitted && s.SelectedDate == shape.TargetDate);

        var forOrganizer = await db.BookingSessions
            .Where(s => s.Status == BookingSessionStatus.Submitted && s.SelectedDate == shape.TargetDate)
            .Join(db.BookingPages, s => s.BookingPageId, p => p.Id, (s, p) => p.OrganizerId)
            .CountAsync(id => id == shape.TargetOrganizerId);

        return new Counts(total, submitted, submittedOnDate, forOrganizer);
    }
}
