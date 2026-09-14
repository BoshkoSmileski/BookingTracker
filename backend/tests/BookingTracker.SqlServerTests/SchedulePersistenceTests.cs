using System.Net;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests;

/// <summary>
/// Owned collections against a real relational store: a working week whose days
/// carry several intervals each, saved and read back through the actual
/// endpoints.
///
/// Two things here can only happen on SQL Server. <c>WorkingDayIntervals</c> and
/// <c>AvailabilityOverrideRanges</c> are keyed by (owner id, IDENTITY int) - a
/// server-generated surrogate the InMemory provider fills in itself and
/// differently. And <c>SaveWorkingScheduleCommandHandler</c> deletes every
/// existing day and inserts the replacements in ONE SaveChangesAsync, against a
/// unique index on (WorkingScheduleId, DayOfWeek): whether that is ordered
/// delete-before-insert is a property of EF's relational batching plus a real
/// constraint, and an unenforced index cannot disagree with it.
/// </summary>
public class SchedulePersistenceTests : SqlServerApiTestBase
{
    private const string Zone = "Europe/Skopje";

    private static string Week(params string[] days) => $$"""{"timeZoneId":"{{Zone}}","days":[{{string.Join(",", days)}}]}""";

    private static string Day(DayOfWeek day, bool enabled, params (string Start, string End)[] intervals)
    {
        var ranges = intervals.Select(i => $$"""{"start":"{{i.Start}}","end":"{{i.End}}"}""");
        return $$"""{"dayOfWeek":{{(int)day}},"isEnabled":{{(enabled ? "true" : "false")}},"intervals":[{{string.Join(",", ranges)}}]}""";
    }

    /// <summary>
    /// One organizer of this test's own. The database is shared by the whole
    /// assembly, so every assertion below is scoped by this schedule id or
    /// organizer id - a bare <c>db.WorkingDays.Count()</c> would be counting
    /// every other test's week as well.
    /// </summary>
    private async Task<(TestData.Workspace Workspace, HttpClient Client)> AnOrganizerAsync()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("sched") + "@example.com", slug: Unique("sched-page"), timeZoneId: Zone));
        return (workspace, ClientFor(workspace.Organizer));
    }

    [Fact]
    public async Task ALunchBreakSurvivesTheRoundTripAsTwoIntervals()
    {
        // The canonical multi-interval day. Both rows share one owner and are
        // distinguished only by the IDENTITY column, so losing one is exactly
        // the failure mode a surrogate-keyed owned collection has.
        var (workspace, client) = await AnOrganizerAsync();
        using var _ = client;

        var response = await client.PutAsync("/api/organizer/availability/schedule", RawJson(Week(
            Day(DayOfWeek.Monday, true, ("09:00:00", "12:00:00"), ("13:00:00", "17:00:00")),
            Day(DayOfWeek.Tuesday, false),
            Day(DayOfWeek.Wednesday, false),
            Day(DayOfWeek.Thursday, false),
            Day(DayOfWeek.Friday, false),
            Day(DayOfWeek.Saturday, false),
            Day(DayOfWeek.Sunday, false))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var read = await ReadJsonAsync(await client.GetAsync("/api/organizer/availability/schedule"));
        var monday = read.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("dayOfWeek").GetInt32() == (int)DayOfWeek.Monday);
        var intervals = monday.GetProperty("intervals").EnumerateArray()
            .Select(i => (i.GetProperty("start").GetString(), i.GetProperty("end").GetString()))
            .ToList();

        Assert.Equal(2, intervals.Count);
        Assert.Contains(("09:00:00", "12:00:00"), intervals);
        Assert.Contains(("13:00:00", "17:00:00"), intervals);

        // And in the table itself, not only in what the read model chose to project.
        var rows = await WithDbAsync(db => db.WorkingDays
            .Where(d => d.WorkingScheduleId == workspace.Schedule.Id && d.DayOfWeek == DayOfWeek.Monday)
            .SelectMany(d => d.Intervals)
            .CountAsync());
        Assert.Equal(2, rows);
    }

    [Fact]
    public async Task SundayIsStillSundayAfterAByteRoundTrip()
    {
        // DayOfWeek is persisted HasConversion<byte>() and .NET numbers Sunday
        // zero, which is also the default value of the column - so a day that
        // came back as Sunday when it was saved as something else, or a Sunday
        // that came back enabled when only Monday was, would both look like
        // ordinary data rather than like a mapping fault.
        var (workspace, client) = await AnOrganizerAsync();
        using var _ = client;

        await client.PutAsync("/api/organizer/availability/schedule", RawJson(Week(
            Day(DayOfWeek.Sunday, true, ("10:00:00", "14:00:00")),
            Day(DayOfWeek.Monday, false),
            Day(DayOfWeek.Tuesday, false),
            Day(DayOfWeek.Wednesday, false),
            Day(DayOfWeek.Thursday, false),
            Day(DayOfWeek.Friday, false),
            Day(DayOfWeek.Saturday, false))));

        var enabled = await WithDbAsync(db => db.WorkingDays
            .Where(d => d.WorkingScheduleId == workspace.Schedule.Id && d.IsEnabled)
            .Select(d => d.DayOfWeek)
            .ToListAsync());

        Assert.Equal(new[] { DayOfWeek.Sunday }, enabled);
    }

    [Fact]
    public async Task ResavingTheWeekReplacesEveryDayWithoutTrippingTheUniqueIndex()
    {
        // SaveWorkingScheduleCommandHandler removes all seven existing days and
        // adds seven replacements in a single SaveChangesAsync, over a unique
        // index on (WorkingScheduleId, DayOfWeek). If EF batched the inserts
        // before the deletes, every one of the seven would collide.
        var (workspace, client) = await AnOrganizerAsync();
        using var _ = client;

        var first = await client.PutAsync("/api/organizer/availability/schedule", RawJson(Week(
            Day(DayOfWeek.Monday, true, ("09:00:00", "17:00:00")),
            Day(DayOfWeek.Tuesday, true, ("09:00:00", "17:00:00")),
            Day(DayOfWeek.Wednesday, false), Day(DayOfWeek.Thursday, false), Day(DayOfWeek.Friday, false),
            Day(DayOfWeek.Saturday, false), Day(DayOfWeek.Sunday, false))));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsync("/api/organizer/availability/schedule", RawJson(Week(
            Day(DayOfWeek.Monday, true, ("08:00:00", "10:00:00"), ("15:00:00", "18:00:00")),
            Day(DayOfWeek.Tuesday, false),
            Day(DayOfWeek.Wednesday, true, ("11:00:00", "12:00:00")),
            Day(DayOfWeek.Thursday, false), Day(DayOfWeek.Friday, false),
            Day(DayOfWeek.Saturday, false), Day(DayOfWeek.Sunday, false))));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await WithDbAsync(async db =>
        {
            // Seven days, not fourteen - and no orphaned intervals left behind
            // by the replaced Monday, which is what the owned collection's
            // cascade is for.
            var days = await db.WorkingDays
                .Where(d => d.WorkingScheduleId == workspace.Schedule.Id)
                .ToListAsync();
            Assert.Equal(7, days.Count);

            var intervals = await db.WorkingDays
                .Where(d => d.WorkingScheduleId == workspace.Schedule.Id)
                .SelectMany(d => d.Intervals)
                .CountAsync();
            Assert.Equal(3, intervals);

            var monday = days.Single(d => d.DayOfWeek == DayOfWeek.Monday);
            Assert.Equal(2, monday.Intervals.Count);
            Assert.False(days.Single(d => d.DayOfWeek == DayOfWeek.Tuesday).IsEnabled);
        });
    }

    [Fact]
    public async Task ReplacingAnOverridesRangesLeavesNoOrphans()
    {
        // The same owned-collection shape as WorkingDay.Intervals, reached
        // through the upsert rather than a wholesale delete - so the old rows
        // are removed by EF's owned-collection handling rather than by the
        // handler, which is a different code path with the same failure mode.
        var (workspace, client) = await AnOrganizerAsync();
        using var _ = client;
        var date = TestData.NextBookableWeekday();

        await client.PutAsync("/api/organizer/availability/overrides", RawJson(
            $$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"09:00:00","end":"10:00:00"},{"start":"11:00:00","end":"12:00:00"},{"start":"14:00:00","end":"15:00:00"}],"note":null}"""));

        await client.PutAsync("/api/organizer/availability/overrides", RawJson(
            $$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"16:00:00","end":"17:00:00"}],"note":null}"""));

        await WithDbAsync(async db =>
        {
            var mine = db.AvailabilityOverrides.Where(o => o.OrganizerId == workspace.Organizer.Id);
            Assert.Single(await mine.ToListAsync());
            Assert.Equal(1, await mine.SelectMany(o => o.Ranges).CountAsync());
        });
    }
}
