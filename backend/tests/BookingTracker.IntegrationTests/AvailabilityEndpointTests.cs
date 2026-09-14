using System.Net;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// The date-exception endpoints: blocked dates (subtractive) and date-specific
/// hours (a second producer of open time), which the organizer edits on one
/// screen but which are two separate resources here.
///
/// What is covered is the endpoint boundary - binding a DateOnly and a TimeOnly
/// out of JSON, the string enum on the exception type, ownership, and the
/// round trip back through GET. The precedence maths itself is
/// SlotGenerationService's and is already tested exhaustively in the unit
/// suite; the two cases repeated here are the ones whose whole point is that
/// they survive the full pipeline.
/// </summary>
public class AvailabilityEndpointTests : ApiTestBase
{
    private const string Slug = "availability-page";

    private async Task<(TestData.Workspace Workspace, HttpClient Client)> ArrangeAsync()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: Slug));
        return (workspace, ClientFor(workspace.Organizer));
    }

    // ---- Blocked dates ------------------------------------------------------

    [Fact]
    public async Task ABlockedDateIsCreatedAndReadBack()
    {
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var created = await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Vacation","reason":"Away"}"""));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var listed = await ReadJsonAsync(await client.GetAsync(
            $"/api/organizer/availability/exceptions?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));

        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal(date.ToString("yyyy-MM-dd"), listed[0].GetProperty("date").GetString());
        Assert.Equal("Vacation", listed[0].GetProperty("type").GetString());
        Assert.Equal("Away", listed[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AMultiDayBlockIsOneRowCoveringBothBoundaryDays()
    {
        // A range is one row, deleted with one delete - the reason EndDate
        // exists. Both boundary days are inclusive.
        var (_, client) = await ArrangeAsync();
        var start = TestData.NextBookableWeekday();
        var end = start.AddDays(4);

        await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""
            {"date":"{{start:yyyy-MM-dd}}","endDate":"{{end:yyyy-MM-dd}}",
             "startTime":null,"endTime":null,"type":"Vacation","reason":"Two weeks off"}
            """));

        Assert.Single(await WithDbAsync(db => db.AvailabilityExceptions.ToListAsync()));

        foreach (var day in new[] { start, end })
        {
            var slots = await BookingFlow.GetSlotsAsync(Client, Slug, day, day);
            Assert.Equal(0, slots.GetArrayLength());
        }
    }

    [Fact]
    public async Task AnUnknownExceptionTypeIsA400()
    {
        // The same string-enum contract as the custom-question type.
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var response = await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Sabbatical","reason":null}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnEndDateBeforeItsStartIsRejected()
    {
        var (_, client) = await ArrangeAsync();
        var start = TestData.NextBookableWeekday();

        var response = await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""
            {"date":"{{start:yyyy-MM-dd}}","endDate":"{{start.AddDays(-3):yyyy-MM-dd}}",
             "startTime":null,"endTime":null,"type":"Vacation","reason":null}
            """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await WithDbAsync(db => db.AvailabilityExceptions.ToListAsync()));
    }

    /// <summary>
    /// Half a time window is a 400 whichever half is missing. Before
    /// the fix the two were asymmetric: end-without-start answered 400 with the
    /// validator's own message, and start-without-end answered <b>500</b>,
    /// because the ordering rule dereferenced EndTime under a When() that only
    /// checked StartTime. Both halves are asserted here precisely because it
    /// was the asymmetry, not the rule, that was wrong.
    /// </summary>
    [Theory]
    [InlineData("\"09:00:00\"", "null")]
    [InlineData("null", "\"17:00:00\"")]
    public async Task HalfATimeWindowIsRejectedWithAMessageRatherThanA500(string startTime, string endTime)
    {
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var response = await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""
            {"date":"{{date:yyyy-MM-dd}}","startTime":{{startTime}},"endTime":{{endTime}},
             "type":"Meeting","reason":null}
            """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Contains(
            "must both be provided",
            problem.GetProperty("errors").EnumerateObject()
                .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString()))
                .Aggregate("", (a, b) => a + b));
        Assert.Empty(await WithDbAsync(db => db.AvailabilityExceptions.ToListAsync()));
    }

    /// <summary>
    /// WorkingDays.DayOfWeek is a tinyint, so an out-of-range value
    /// was not rejected by the column - it was truncated into it. Measured
    /// before the fix: -3 answered 200, echoed -3 on the response, and was
    /// stored and re-read as 253.
    /// </summary>
    [Theory]
    [InlineData(-3)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(300)]
    public async Task ADayOutsideTheWeekIsRejectedRatherThanTruncatedIntoTheColumn(int dayOfWeek)
    {
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PutAsync("/api/organizer/availability/schedule",
            RawJson($$"""
            {"timeZoneId":"UTC","days":[
              {"dayOfWeek":{{dayOfWeek}},"isEnabled":true,"intervals":[{"start":"09:00:00","end":"17:00:00"}]}]}
            """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The seeded week is still exactly the seven real days - the rejected
        // save replaced nothing.
        var days = await WithDbAsync(db => db.WorkingDays
            .Where(d => d.WorkingScheduleId == workspace.Schedule.Id)
            .Select(d => d.DayOfWeek).ToListAsync());
        Assert.Equal(7, days.Count);
        Assert.All(days, d => Assert.True(Enum.IsDefined(d), $"Persisted a DayOfWeek of {(int)d}."));
    }

    [Fact]
    public async Task ABlockedDateCanBeDeletedByItsOwner()
    {
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var created = await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Holiday","reason":null}"""));
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/organizer/availability/exceptions/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await WithDbAsync(db => db.AvailabilityExceptions.ToListAsync()));
    }

    // ---- Date-specific hours (overrides) ------------------------------------

    [Fact]
    public async Task AnOverrideIsSavedAndReadBack()
    {
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var response = await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""
            {"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"13:00:00","end":"17:00:00"}],"note":"Late start"}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listed = await ReadJsonAsync(await client.GetAsync(
            $"/api/organizer/availability/overrides?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));

        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal("Late start", listed[0].GetProperty("note").GetString());
        Assert.Equal("13:00:00", listed[0].GetProperty("ranges")[0].GetProperty("start").GetString());
    }

    [Fact]
    public async Task AnOverrideReplacesTheWeeklyHoursForThatDateOnly()
    {
        // Replaces, never merges - and only on its own date.
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"13:00:00","end":"15:00:00"}],"note":null}"""));

        var overridden = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        var times = Enumerable.Range(0, overridden.GetArrayLength())
            .Select(i => overridden[i].GetProperty("localStartTime").GetString()).ToList();

        Assert.Equal("13:00:00", times.First());
        Assert.DoesNotContain("09:00:00", times);

        // The next working day still follows the ordinary week.
        var untouched = await BookingFlow.GetSlotsAsync(Client, Slug, date.AddDays(1), date.AddDays(1));
        if (untouched.GetArrayLength() > 0)
        {
            Assert.Equal("09:00:00", untouched[0].GetProperty("localStartTime").GetString());
        }
    }

    [Fact]
    public async Task AnOverrideCanOpenANormallyClosedSaturday()
    {
        // The case no subtractive concept could ever express, which is why
        // overrides exist as a second producer at all - asserted here through
        // the endpoint rather than against the service.
        var (_, client) = await ArrangeAsync();
        var saturday = TestData.NextClosedSaturday();

        Assert.Equal(0, (await BookingFlow.GetSlotsAsync(Client, Slug, saturday, saturday)).GetArrayLength());

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{saturday:yyyy-MM-dd}}","ranges":[{"start":"10:00:00","end":"12:00:00"}],"note":null}"""));

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, saturday, saturday);
        Assert.True(slots.GetArrayLength() > 0);
        Assert.Equal("10:00:00", slots[0].GetProperty("localStartTime").GetString());
    }

    [Fact]
    public async Task AWholeDayBlockStillWinsOverAnOverride()
    {
        // The precedence rule the consolidated screen warns about: an override
        // says when the day is OPEN, and a block then subtracts from it.
        var (_, client) = await ArrangeAsync();
        var saturday = TestData.NextClosedSaturday();

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{saturday:yyyy-MM-dd}}","ranges":[{"start":"10:00:00","end":"12:00:00"}],"note":null}"""));
        Assert.True((await BookingFlow.GetSlotsAsync(Client, Slug, saturday, saturday)).GetArrayLength() > 0);

        await client.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{saturday:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Holiday","reason":null}"""));

        Assert.Equal(0, (await BookingFlow.GetSlotsAsync(Client, Slug, saturday, saturday)).GetArrayLength());
    }

    [Fact]
    public async Task SavingAnOverrideTwiceUpdatesTheSameRowRatherThanAdding()
    {
        // One row per date, enforced by a unique index - which is why the write
        // side is a single upsert rather than a Create/Update pair.
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"09:00:00","end":"11:00:00"}],"note":null}"""));
        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"14:00:00","end":"16:00:00"}],"note":null}"""));

        var rows = await WithDbAsync(db => db.AvailabilityOverrides.ToListAsync());
        Assert.Single(rows);

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        Assert.Equal("14:00:00", slots[0].GetProperty("localStartTime").GetString());
    }

    [Fact]
    public async Task AnEmptyOverrideClosesTheDay()
    {
        // Saving no ranges is "closed", and is deliberately a different
        // operation from deleting the override.
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[],"note":"Closed today"}"""));

        Assert.Equal(0, (await BookingFlow.GetSlotsAsync(Client, Slug, date, date)).GetArrayLength());
    }

    [Fact]
    public async Task DeletingAnOverrideRestoresTheWeeklySchedule()
    {
        var (_, client) = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[],"note":null}"""));
        Assert.Equal(0, (await BookingFlow.GetSlotsAsync(Client, Slug, date, date)).GetArrayLength());

        var id = (await WithDbAsync(db => db.AvailabilityOverrides.SingleAsync())).Id;
        var response = await client.DeleteAsync($"/api/organizer/availability/overrides/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        Assert.True(slots.GetArrayLength() > 0);
        Assert.Equal("09:00:00", slots[0].GetProperty("localStartTime").GetString());
    }

    // ---- Ownership ----------------------------------------------------------

    [Fact]
    public async Task AnotherOrganizerCannotDeleteAnOverride()
    {
        var (_, client) = await ArrangeAsync();
        var intruder = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "intruder@example.com", slug: "intruder-page"));
        using var intruderClient = ClientFor(intruder.Organizer);

        var date = TestData.NextBookableWeekday();
        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[{"start":"13:00:00","end":"17:00:00"}],"note":null}"""));
        var id = (await WithDbAsync(db => db.AvailabilityOverrides.SingleAsync())).Id;

        var response = await intruderClient.DeleteAsync($"/api/organizer/availability/overrides/{id}");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected the delete to be refused, got {(int)response.StatusCode}.");
        Assert.Single(await WithDbAsync(db => db.AvailabilityOverrides.ToListAsync()));
    }

    [Fact]
    public async Task AnOverrideAppliesToEveryPageTheOrganizerOwns()
    {
        // Overrides are organizer-scoped, not per page - so a second page sees
        // the same change.
        var (workspace, client) = await ArrangeAsync();
        await WithDbAsync(async db =>
        {
            var second = BookingTracker.Domain.Entities.BookingPage.Create(
                workspace.Organizer.Id, "second-page", "Second", 30, 0, 0);
            db.BookingPages.Add(second);
            await db.SaveChangesAsync();
        });

        var saturday = TestData.NextClosedSaturday();
        await client.PutAsync("/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{saturday:yyyy-MM-dd}}","ranges":[{"start":"10:00:00","end":"12:00:00"}],"note":null}"""));

        var slots = await BookingFlow.GetSlotsAsync(Client, "second-page", saturday, saturday);
        Assert.True(slots.GetArrayLength() > 0);
    }
}
