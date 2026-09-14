using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.MigrationTests;

/// <summary>
/// The claim this whole project exists to make:
///
///   a real database containing data created under an older version of
///   BookingTracker can be upgraded to the current schema without losing,
///   corrupting or invalidating that data.
///
/// Everything here runs against the database <see cref="LegacyDatabaseFixture"/>
/// built at <see cref="LegacySchema.Baseline"/>, populated, and then upgraded
/// with the production <c>MigrateAsync()</c> call. See that fixture for the
/// runtime proofs that the database really was old and really was a throwaway.
///
/// **Five tests, not fifty.** Each one is a distinct claim about the upgrade -
/// that it ran, that nothing was lost, that the backfill is right, that the new
/// columns are right, and that the schema is right. Splitting them further would
/// add names without adding coverage, since they all share one migration run.
/// The sixth claim - that the application can actually use the result - is a
/// separate class, because it needs the web host.
/// </summary>
[Collection(MigrationCollection.Name)]
public class PopulatedDatabaseMigrationTests(LegacyDatabaseFixture fixture)
{
    // ---- 1. the migration itself --------------------------------------------

    [Fact]
    public async Task MigrationAppliesToAPopulatedDatabase()
    {
        Assert.True(fixture.MigrationFailure is null,
            $"MigrateAsync() failed on the populated legacy database: {fixture.MigrationFailure}");

        var applied = await SchemaProbe.AppliedMigrationsAsync();

        // Every migration this suite claims to exercise is now recorded as
        // applied, in order, on top of the ones that were there before.
        Assert.Equal([.. fixture.MigrationsBeforeUpgrade, .. LegacySchema.Applied], applied);

        await using var db = LegacyDatabaseFixture.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    /// A guard on this suite's own honesty rather than on the application: if a
    /// migration is added to the repository and nobody decides whether it
    /// carries data risk, <see cref="LegacySchema.Applied"/> stops describing
    /// what is actually being upgraded, and every claim above quietly narrows.
    /// </summary>
    [Fact]
    public async Task TheTestedRangeIsEveryMigrationAfterTheBaseline()
    {
        await using var db = LegacyDatabaseFixture.CreateContext();
        var all = db.Database.GetMigrations().ToList();

        var baselineIndex = all.IndexOf(LegacySchema.Baseline);
        Assert.True(baselineIndex >= 0, $"{LegacySchema.Baseline} is no longer a migration in this assembly.");

        Assert.Equal(all[(baselineIndex + 1)..], LegacySchema.Applied);
    }

    // ---- 2. nothing was lost -------------------------------------------------

    [Fact]
    public async Task EveryLegacyRowAndEveryLegacyIdSurvives()
    {
        // First: the seed really was what this suite thinks it was. Without
        // this, a seed that silently inserted nothing would make every
        // "unchanged" assertion below trivially true.
        Assert.Equal(LegacyData.ExpectedRowCounts.OrderBy(e => e.Key), fixture.Snapshot.RowCounts.OrderBy(e => e.Key));

        foreach (var table in LegacySnapshot.Tables)
        {
            var after = await SchemaProbe.RowCountAsync(table);
            Assert.True(fixture.Snapshot.RowCounts[table] == after,
                $"{table}: {fixture.Snapshot.RowCounts[table]} rows before the migration, {after} after.");
        }

        // Ids, not just counts - a migration that deleted one row and inserted
        // another would keep the count and lose the data.
        await using var db = LegacyDatabaseFixture.CreateContext();

        await AssertSameIdsAsync("Organizers", db.Organizers.Select(x => x.Id));
        await AssertSameIdsAsync("BookingPages", db.BookingPages.Select(x => x.Id));
        await AssertSameIdsAsync("WorkingSchedules", db.WorkingSchedules.Select(x => x.Id));
        await AssertSameIdsAsync("WorkingDays", db.WorkingDays.Select(x => x.Id));
        await AssertSameIdsAsync("AvailabilityExceptions", db.AvailabilityExceptions.Select(x => x.Id));
        await AssertSameIdsAsync("BookingSessions", db.BookingSessions.Select(x => x.Id));
        await AssertSameIdsAsync("RefreshTokens", db.RefreshTokens.Select(x => x.Id));

        // The values a booking is actually made of, not only its id.
        foreach (var (id, before) in fixture.Snapshot.Sessions)
        {
            var after = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == id);

            Assert.Equal(before.BookingPageId, after.BookingPageId);
            Assert.Equal((BookingSessionStatus)before.Status, after.Status);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Email, after.Email);
            Assert.Equal(before.SelectedDate, after.SelectedDate);
            Assert.Equal(before.SelectedTime, after.SelectedTime);
            Assert.Equal(before.BookingReference, after.BookingReference);
            Assert.Equal(before.PublicToken, after.PublicToken);
            Assert.Equal(before.RescheduleCount, after.RescheduleCount);
            Assert.Equal(before.SubmittedAt, after.SubmittedAt);
            Assert.Equal(before.CancelledAt, after.CancelledAt);
            Assert.Equal(before.CancelledBy.HasValue ? (CancelledByType)before.CancelledBy.Value : null, after.CancelledBy);
        }

        // The append-only log, in the order Rebuild() sorts on.
        var events = await db.BookingSessionEvents.AsNoTracking()
            .Where(e => e.SessionId == LegacyData.SubmittedSessionId)
            .OrderBy(e => e.ClientSequenceNumber)
            .Select(e => new { e.ClientSequenceNumber, e.EventType, e.FieldName, e.NewValue })
            .ToListAsync();

        Assert.Equal(
            fixture.Snapshot.Events.Select(e => (e.Sequence, (BookingEventType)e.EventType, e.FieldName, e.NewValue)),
            events.Select(e => (e.ClientSequenceNumber, e.EventType, e.FieldName, e.NewValue)));

        // The foreign keys still resolve: an orphaned child is a survived row
        // that means nothing.
        var syncedEvent = await db.CalendarSyncedEvents.AsNoTracking().SingleAsync();
        Assert.Equal(LegacyData.SubmittedSessionId, syncedEvent.BookingSessionId);
        Assert.Equal(LegacyData.AdaCalendarConnectionId, syncedEvent.CalendarConnectionId);
        Assert.True(await db.BookingSessions.AnyAsync(s => s.Id == syncedEvent.BookingSessionId));
        Assert.True(await db.CalendarConnections.AnyAsync(c => c.Id == syncedEvent.CalendarConnectionId));

        async Task AssertSameIdsAsync(string table, IQueryable<Guid> ids)
            => Assert.Equal(
                fixture.Snapshot.Ids[table].Order(),
                (await ids.ToListAsync()).Order());
    }

    // ---- 3. the backfill -----------------------------------------------------

    /// <summary>
    /// The single highest-risk statement in the whole migration history:
    /// <c>UPDATE [AvailabilityExceptions] SET [EndDate] = [Date]</c>.
    ///
    /// The column is added non-nullable with the scaffolded default of year 1,
    /// so without that UPDATE every pre-existing row would hold EndDate &lt;
    /// Date - and <see cref="AvailabilityException.Covers"/>, which is the one
    /// place that knows the range is inclusive at both ends, would then report
    /// that the exception applies to no date at all. The failure mode is not an
    /// error; it is every previously blocked date silently becoming bookable.
    ///
    /// So the assertion is made twice over: on the stored value, and on the
    /// behaviour the application derives from it.
    /// </summary>
    [Fact]
    public async Task AvailabilityExceptionEndDateIsBackfilledToItsOwnStartDate()
    {
        await using var db = LegacyDatabaseFixture.CreateContext();

        var exceptions = await db.AvailabilityExceptions.AsNoTracking().ToListAsync();
        Assert.Equal(fixture.Snapshot.Exceptions.Count, exceptions.Count);

        foreach (var after in exceptions)
        {
            var before = fixture.Snapshot.Exceptions[after.Id];

            // Everything the legacy row already said is unchanged.
            Assert.Equal(before.OrganizerId, after.OrganizerId);
            Assert.Equal(before.Date, after.Date);
            Assert.Equal(before.StartTime, after.StartTime);
            Assert.Equal(before.EndTime, after.EndTime);
            Assert.Equal((AvailabilityExceptionType)before.Type, after.Type);
            Assert.Equal(before.Reason, after.Reason);

            // And the new column says what the migration's own comment says it
            // must: a row written before ranges existed was a single day.
            Assert.Equal(before.Date, after.EndDate);
            Assert.True(after.IsSingleDay);
            Assert.Equal(1, after.TotalDays);

            // The behaviour, through the real domain method rather than a
            // re-derivation of it.
            Assert.True(after.Covers(before.Date), $"{after.Id} no longer covers its own date {before.Date:yyyy-MM-dd}.");
            Assert.False(after.Covers(before.Date.AddDays(-1)));
            Assert.False(after.Covers(before.Date.AddDays(1)));
        }

        // The corruption the backfill exists to prevent, asserted as an absence
        // over the whole table rather than row by row - so a row this suite
        // never seeded could not slip through either.
        Assert.Equal(0, await SchemaProbe.ScalarAsync<int>(
            "SELECT COUNT(*) FROM AvailabilityExceptions WHERE EndDate < Date;"));
        Assert.Equal(0, await SchemaProbe.ScalarAsync<int>(
            "SELECT COUNT(*) FROM AvailabilityExceptions WHERE YEAR(EndDate) = 1;"));

        // The timed exception keeps being a timed exception: the backfill is
        // about dates and must not have touched the window.
        var timed = exceptions.Single(e => e.Id == LegacyData.TimedExceptionId);
        Assert.False(timed.IsWholeDay);
        Assert.Equal(new TimeOnly(10, 0), timed.StartTime);
        Assert.Equal(new TimeOnly(12, 0), timed.EndTime);

        // And a past-dated row is repaired exactly like a future one - the
        // UPDATE has no WHERE clause, and it should not have.
        var past = exceptions.Single(e => e.Id == LegacyData.PastExceptionId);
        Assert.Equal(past.Date, past.EndDate);
    }

    // ---- 4. columns added to populated tables --------------------------------

    [Fact]
    public async Task ColumnsAddedToPopulatedTablesCarryTheirDeclaredValues()
    {
        await using var db = LegacyDatabaseFixture.CreateContext();

        // The narrowed column. Legacy rows lived in nvarchar(max); this one held
        // exactly the 2000 characters the new limit allows, which is the value
        // most likely to be truncated by a careless narrowing.
        var submitted = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == LegacyData.SubmittedSessionId);
        Assert.Equal(fixture.Snapshot.Sessions[LegacyData.SubmittedSessionId].Message, submitted.Message);
        Assert.Equal(2000, submitted.Message!.Length);

        // A null stays null rather than becoming an empty string.
        var active = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == LegacyData.ActiveSessionId);
        Assert.Null(active.Message);

        // AddBookingMeetingProvider: non-nullable with default 0 on a table that
        // already had three rows. None of them may end up as GoogleMeet.
        var pages = await db.BookingPages.AsNoTracking().ToListAsync();
        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.Equal(MeetingProviderType.None, page.MeetingProvider));

        // The nullable half of the same migration: a legacy booking has no
        // meeting, and must not be given one.
        var sessions = await db.BookingSessions.AsNoTracking().ToListAsync();
        Assert.All(sessions, session =>
        {
            Assert.Null(session.MeetingProvider);
            Assert.Null(session.MeetingUrl);
        });

        // Tables introduced in the range exist and are empty - a legacy database
        // genuinely has no notification settings row (the application falls back
        // to defaults in memory, which is why no backfill was needed), no
        // reminders, no custom questions and no date overrides.
        Assert.Empty(await db.NotificationSettings.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EmailNotifications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.BookingReminders.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AvailabilityOverrides.AsNoTracking().ToListAsync());
    }

    // ---- 5. the schema -------------------------------------------------------

    /// <summary>
    /// Read from <c>sys.columns</c>/<c>sys.indexes</c> rather than from the EF
    /// model: the model and the snapshot are both derived from the same C#, so
    /// comparing them to each other cannot detect a migration that failed to
    /// bring a real database into line with either.
    ///
    /// Scoped to what the tested migrations actually declare. Asserting the
    /// whole schema would be a second copy of the model snapshot.
    /// </summary>
    [Fact]
    public async Task SchemaAfterTheUpgradeIsWhatTheMigrationsDeclare()
    {
        // AddAvailabilityExceptionEndDate
        var endDate = await SchemaProbe.ColumnAsync("AvailabilityExceptions", "EndDate");
        Assert.NotNull(endDate);
        Assert.Equal("date", endDate.Type);
        Assert.False(endDate.IsNullable);

        var rangeIndex = await SchemaProbe.IndexAsync("AvailabilityExceptions", "IX_AvailabilityExceptions_OrganizerId_Date_EndDate");
        Assert.NotNull(rangeIndex);
        Assert.Equal(["OrganizerId", "Date", "EndDate"], rangeIndex.KeyColumns);
        Assert.Null(await SchemaProbe.IndexAsync("AvailabilityExceptions", "IX_AvailabilityExceptions_OrganizerId_Date"));

        // AddBookingSessionMessageMaxLength
        var message = await SchemaProbe.ColumnAsync("BookingSessions", "Message");
        Assert.NotNull(message);
        Assert.Equal("nvarchar", message.Type);
        Assert.Equal(2000, message.MaxCharacters);
        Assert.True(message.IsNullable);

        // AddBookingMeetingProvider
        var pageProvider = await SchemaProbe.ColumnAsync("BookingPages", "MeetingProvider");
        Assert.NotNull(pageProvider);
        Assert.False(pageProvider.IsNullable);
        Assert.True((await SchemaProbe.ColumnAsync("BookingSessions", "MeetingProvider"))!.IsNullable);

        // AddBookingReminders - the filtered unique index that is THE
        // duplicate-delivery guarantee. Its filter is the load-bearing half:
        // without it a rescheduled booking could not keep a sent reminder for
        // the old time alongside a fresh one for the new.
        var reminderIndex = await SchemaProbe.IndexAsync("BookingReminders", "UX_BookingReminders_Session_Offset_Scheduled");
        Assert.NotNull(reminderIndex);
        Assert.True(reminderIndex.IsUnique);
        Assert.Equal(["BookingSessionId", "MinutesBeforeEvent"], reminderIndex.KeyColumns);
        Assert.Equal("([Status]=(0))", reminderIndex.Filter?.Replace(" ", string.Empty));

        // NarrowBookingConflictLockFootprint - rebuilt over a table that already
        // held six rows. SelectedTime must be INCLUDED rather than a key column:
        // that is what keeps the index covering without widening the range lock.
        var conflictIndex = await SchemaProbe.IndexAsync("BookingSessions", "IX_BookingSessions_BookingPageId_Status_SelectedDate");
        Assert.NotNull(conflictIndex);
        Assert.Equal(["BookingPageId", "Status", "SelectedDate"], conflictIndex.KeyColumns);
        Assert.Equal(["SelectedTime"], conflictIndex.IncludedColumns);
        Assert.Null(await SchemaProbe.IndexAsync("BookingSessions", "IX_BookingSessions_BookingPageId_Status"));

        // AddAvailabilityOverrides - one override per organizer per date, which
        // is a constraint rather than application care because ResolveOpenRanges
        // takes the first match.
        var overrideIndex = await SchemaProbe.IndexAsync("AvailabilityOverrides", "IX_AvailabilityOverrides_OrganizerId_Date");
        Assert.NotNull(overrideIndex);
        Assert.True(overrideIndex.IsUnique);
        Assert.Equal(["OrganizerId", "Date"], overrideIndex.KeyColumns);
        Assert.True(await SchemaProbe.ForeignKeyCascadesOnDeleteAsync(
            "AvailabilityOverrideRanges", "FK_AvailabilityOverrideRanges_AvailabilityOverrides_AvailabilityOverrideId"));

        // Every table the range introduces.
        foreach (var table in new[]
        {
            "EmailNotifications", "NotificationSettings", "BookingReminders",
            "BookingFormFields", "BookingSessionAnswers",
            "AvailabilityOverrides", "AvailabilityOverrideRanges",
        })
        {
            Assert.True(await SchemaProbe.TableExistsAsync(table), $"{table} was not created by the upgrade.");
        }
    }
}
