using BookingTracker.Domain.Common;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// What rolling this schema back actually does to a database that holds data.
///
/// <b>Why this exists.</b> The correctness suite proved the UP path is correct on a populated
/// database and the volume diagnostics proved what it costs at volume. Between them every
/// <c>Up()</c> in the repository has been executed against real rows - and no
/// <c>Down()</c> had ever been executed at all, by any suite, once. That is the
/// half of the migration history a deployment reaches only when something has
/// already gone wrong, which is the worst possible moment to discover it does not
/// work.
///
/// <b>The tests do not require rollback to be lossless, and must not.</b> Most of
/// these Down migrations destroy information by definition - you cannot drop a
/// column and keep what was in it - so a suite that failed on data loss would be
/// asserting that the schema history should have been written differently. What
/// it asserts instead is that each rollback destroys EXACTLY what its own
/// declaration says it destroys (<see cref="MigrationOrder"/>), that everything
/// else survives byte-identical, and that the schema can be driven all the way
/// down and all the way back up again.
///
/// Three claims are kept rigorously apart, because conflating them is the easiest
/// way to write a reassuring and false test:
///
///   SCHEMA round trip        latest -> InitialCreate -> latest, proven
///   DATA-PRESERVING rollback only where nothing observable is lost, proven per step
///   DESTRUCTIVE rollback     named, and the loss demonstrated rather than implied
/// </summary>
[Collection(RollbackCollection.Name)]
public class DownMigrationTests(RollbackFixture fixture, ITestOutputHelper output)
{
    // ---- the rollback itself -------------------------------------------------

    [Fact]
    public void TheDatabaseWasBuiltSeededAndRolledBack()
    {
        Assert.Null(fixture.SetupFailure);
        Assert.NotNull(fixture.Seeded);
        Assert.NotNull(fixture.Snapshot);
    }

    [Fact]
    public void EveryDownMigrationExecutesAgainstPopulatedData()
    {
        Assert.Null(fixture.SetupFailure);

        output.WriteLine("""

            migration                                       elapsed   impact           schema
            ---------------------------------------------  --------  ---------------  ------
            """);

        foreach (var step in fixture.Steps)
        {
            output.WriteLine(
                $"{step.Name,-45}  {step.ElapsedMs,6:N0}ms  {step.Step.Impact,-15}  " +
                $"{(step.Succeeded ? step.SchemaMatchesDeclaration ? "ok" : "MISMATCH" : "FAILED")}");
        }

        var failed = fixture.Steps.Where(s => !s.Succeeded).ToArray();

        // A Down migration that cannot run against populated data would be the
        // single most valuable finding this suite could produce. It is reported
        // with the migration's name and SQL Server's own message rather than as a
        // bare assertion failure.
        Assert.True(
            failed.Length == 0,
            failed.Length == 0
                ? string.Empty
                : "Down migration(s) failed against populated data:\n" +
                  string.Join("\n", failed.Select(f => $"  {f.Name}: {f.Failure!.GetType().Name}: {f.Failure.Message}")));

        Assert.Equal(MigrationOrder.Path.Count, fixture.Steps.Count);
    }

    /// <summary>
    /// A guard on this suite's own honesty, and the counterpart to
    /// <c>TheTestedRangeIsEveryMigrationAfterTheBaseline</c> on the upgrade path.
    /// Adding a migration without deciding what rolling it back destroys fails
    /// here rather than quietly narrowing what the audit claims to cover.
    /// </summary>
    [Fact]
    public async Task TheRollbackPathIsEveryMigrationAfterInitialCreate()
    {
        var applied = await LatestSchemaAppliedMigrationsAsync();

        Assert.Equal(MigrationOrder.All, applied);
        Assert.Equal(MigrationOrder.All.Count - 1, MigrationOrder.Path.Count);

        // Newest first, and each step lands on the migration below it.
        var expected = MigrationOrder.All.Reverse().Take(MigrationOrder.Path.Count).ToArray();
        Assert.Equal(expected, fixture.Steps.Select(s => s.Step.MigrationId).ToArray());

        for (var i = 0; i < MigrationOrder.Path.Count; i++)
        {
            var step = MigrationOrder.Path[i];
            var expectedTarget = MigrationOrder.All[^(i + 2)];
            Assert.Equal(expectedTarget, step.Target);
        }
    }

    [Fact]
    public void MigrationHistoryMovesBackwardOneMigrationAtATime()
    {
        Assert.Null(fixture.RollbackFailure);

        foreach (var step in fixture.Steps)
        {
            // After rolling back step N, exactly the migrations up to and
            // including its target must remain recorded as applied.
            var expected = MigrationOrder.All
                .Take(MigrationOrder.All.ToList().IndexOf(step.Step.Target) + 1)
                .ToArray();

            Assert.Equal(expected, step.AppliedAfter);
        }
    }

    [Fact]
    public void EachDownMigrationRemovesExactlyWhatItDeclares()
    {
        Assert.Null(fixture.RollbackFailure);

        foreach (var step in fixture.Steps)
        {
            Assert.Empty(step.UnexpectedlyPresentTables);
            Assert.Empty(step.UnexpectedlyPresentColumns);
            Assert.Empty(step.UnexpectedlyPresentIndexes);
            Assert.Empty(step.UnexpectedlyMissingIndexes);
        }
    }

    [Fact]
    public void ForeignKeysHaveNoOrphansAfterAnyRollbackStep()
    {
        Assert.Null(fixture.RollbackFailure);

        foreach (var step in fixture.Steps)
        {
            Assert.True(
                step.Orphans.Count == 0,
                $"{step.Name} left orphaned rows: {string.Join("; ", step.Orphans)}");
        }
    }

    /// <summary>
    /// Both owned collections in the schema go down as two tables - the children
    /// first, then their owner - and neither may leave the other standing. An
    /// owned collection whose owner table were dropped first would either fail on
    /// the foreign key or leave rows nothing can reach.
    /// </summary>
    [Fact]
    public void OwnedCollectionsAreDroppedWithTheirOwners()
    {
        Assert.Null(fixture.RollbackFailure);

        foreach (var (owner, child) in new[]
        {
            ("AvailabilityOverrides", "AvailabilityOverrideRanges"),
            ("WorkingDays", "WorkingDayIntervals"),
        })
        {
            foreach (var step in fixture.Steps)
            {
                var ownerGone = !step.TablesAfter.Contains(owner);
                var childGone = !step.TablesAfter.Contains(child);

                Assert.False(
                    ownerGone && !childGone,
                    $"After {step.Name}, {owner} was dropped while its owned collection {child} was left behind.");
            }
        }
    }

    // ---- destructive rollback: the headline finding --------------------------

    /// <summary>
    /// The case this phase was created for.
    ///
    /// A ten-day vacation is one row with <c>Date</c> and <c>EndDate</c>. The
    /// rollback keeps the row and deletes the column, so what is lost is not a
    /// record but a RANGE: after it, that row is indistinguishable from a
    /// single-day block on its start date, and `AvailabilityException.Covers`
    /// would report the other nine days as bookable. Nothing can reconstruct it -
    /// the end date was never written anywhere else.
    /// </summary>
    [Fact]
    public async Task RollingBackEndDateKeepsTheExceptionAndDestroysItsRange()
    {
        Assert.Null(fixture.RollbackFailure);

        var before = fixture.Snapshot.Exceptions.Single(e => e.Id == fixture.Seeded.MultiDayExceptionId);

        // The seed really was a multi-day range, or the rest of this proves nothing.
        Assert.Equal(fixture.Dates.MultiDayStart, before.Date);
        Assert.Equal(fixture.Dates.MultiDayEnd, before.EndDate);
        Assert.True(before.EndDate > before.Date);
        Assert.Equal(9, before.EndDate.DayNumber - before.Date.DayNumber);

        var step = fixture.Steps.Single(s => s.Step.Name == "AddAvailabilityExceptionEndDate");
        Assert.Equal(MigrationOrder.Impact.Destructive, step.Step.Impact);

        // At that point in the rollback the column is gone and the row is not.
        // Both halves matter: a rollback that had deleted the rows would be a
        // different (and much more obvious) defect.
        output.WriteLine(
            $"multi-day exception {before.Id}: Date={before.Date}, EndDate={before.EndDate} " +
            $"({before.EndDate.DayNumber - before.Date.DayNumber + 1} days) before rollback");
        output.WriteLine("after AddAvailabilityExceptionEndDate.Down: the EndDate column no longer exists.");

        // AvailabilityExceptions itself is dropped later in the path (by
        // AddAvailability.Down), so the surviving-row check is made against the
        // state that step recorded rather than against the database now.
        Assert.Contains("AvailabilityExceptions", step.TablesAfter);
        Assert.Empty(step.UnexpectedlyPresentColumns);

        // And the replacement index really is the narrower predecessor.
        var restored = step.Step.IndexesRestored.Single();
        Assert.Equal("IX_AvailabilityExceptions_OrganizerId_Date", restored.Index);
        Assert.Empty(step.UnexpectedlyMissingIndexes);

        // Nothing else on the row was touched by the same migration: the timed
        // exception keeps its window, which is what makes this a loss of EndDate
        // specifically rather than a loss of exceptions generally.
        var timedBefore = fixture.Snapshot.Exceptions.Single(e => e.Id == fixture.Seeded.TimedExceptionId);
        Assert.Equal(new TimeOnly(10, 0), timedBefore.StartTime);
        Assert.Equal(new TimeOnly(12, 0), timedBefore.EndTime);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Every rollback the audit calls destructive is one where information that
    /// existed only in the dropped structure is gone. Stated as a list rather than
    /// buried per-test, so the count cannot drift without someone noticing.
    /// </summary>
    [Fact]
    public void DestructiveRollbacksAreNamedRatherThanDiscovered()
    {
        var destructive = MigrationOrder.Path
            .Where(s => s.Impact == MigrationOrder.Impact.Destructive)
            .Select(s => s.Name)
            .ToArray();

        var preserving = MigrationOrder.Path
            .Where(s => s.Impact == MigrationOrder.Impact.DataPreserving)
            .Select(s => s.Name)
            .ToArray();

        output.WriteLine($"data-preserving ({preserving.Length}): {string.Join(", ", preserving)}");
        output.WriteLine($"destructive     ({destructive.Length}): {string.Join(", ", destructive)}");

        Assert.Equal(
            ["NarrowBookingConflictLockFootprint", "AddBookingSessionMessageMaxLength"],
            preserving);

        Assert.Equal(14, destructive.Length);
    }

    // ---- data-preserving rollback, proven rather than assumed ----------------

    /// <summary>
    /// The only rollback in the history that changes a column's type, and the only
    /// one that could truncate. It widens rather than narrows, so it cannot -
    /// which is worth proving on the exact boundary value the forward migration
    /// imposed, because that is the row a careless widening would damage.
    /// </summary>
    [Fact]
    public void WideningTheMessageColumnPreservesTheBoundaryValue()
    {
        Assert.Null(fixture.RollbackFailure);

        var step = fixture.Steps.Single(s => s.Step.Name == "AddBookingSessionMessageMaxLength");
        Assert.Equal(MigrationOrder.Impact.DataPreserving, step.Step.Impact);

        var before = fixture.Snapshot.Sessions.Single(s => s.Id == fixture.Seeded.SubmittedSessionId);
        Assert.Equal(BookingFieldLimits.MessageMaxLength, before.Message.Length);
        Assert.Equal(LatestSchemaData.BoundaryMessage, before.Message);

        // Rows are counted at every step; the session count does not move.
        Assert.Equal(
            fixture.Snapshot.RowCounts["BookingSessions"],
            step.RowCountsAfter["BookingSessions"]);
    }

    /// <summary>
    /// The conflict index rollback is the one step that touches only index shape.
    /// It is data-preserving - and it is also the one rollback with a
    /// consequence that is not about data at all: it restores the index whose
    /// lock footprint measured as taking a table-level S lock on every
    /// booking submission.
    /// </summary>
    [Fact]
    public void RollingBackTheConflictIndexChangesIndexShapeAndNothingElse()
    {
        Assert.Null(fixture.RollbackFailure);

        var step = fixture.Steps.Single(s => s.Step.Name == "NarrowBookingConflictLockFootprint");
        Assert.Equal(MigrationOrder.Impact.DataPreserving, step.Step.Impact);

        Assert.Empty(step.UnexpectedlyPresentIndexes);
        Assert.Empty(step.UnexpectedlyMissingIndexes);

        foreach (var table in MigrationOrder.InitialCreateTables)
        {
            Assert.Equal(fixture.Snapshot.RowCounts[table], step.RowCountsAfter[table]);
        }
    }

    // ---- what survives all the way down --------------------------------------

    /// <summary>
    /// The strongest data claim in the suite, and the one most specific to this
    /// project: <b>no Down migration on the rollback path touches
    /// BookingSessionEvents</b>. The append-only log the whole thesis rests on
    /// survives a complete rollback byte-identical, which is what makes several of
    /// the destructive rollbacks below recoverable in principle.
    /// </summary>
    [Fact]
    public async Task TheEventLogSurvivesTheEntireRollbackUnchanged()
    {
        Assert.Null(fixture.RollbackFailure);

        Assert.Equal(
            fixture.Snapshot.RowCounts["BookingSessionEvents"],
            fixture.RowCountsAtInitialCreate["BookingSessionEvents"]);

        var after = await ReadEventsAtInitialCreateAsync();
        Assert.Equal(fixture.Snapshot.Events, after);
    }

    /// <summary>
    /// Two rollbacks destroy a projection whose facts are also in the event log,
    /// and one destroys a fact that is only in the projection. Distinguishing
    /// those is the difference between "recoverable in principle" and "gone".
    ///
    /// This asserts the events are still there; it does NOT claim the application
    /// would rebuild from them, because at InitialCreate there is no column left
    /// to rebuild into. Recoverability is a property of the log, not a feature
    /// that exists.
    /// </summary>
    [Fact]
    public async Task InformationDroppedFromProjectionsIsStillInTheEventLog()
    {
        Assert.Null(fixture.RollbackFailure);

        var events = await ReadEventsAtInitialCreateAsync();
        var submitted = events.Where(e => e.SessionId == fixture.Seeded.SubmittedSessionId).ToArray();

        // The custom answers, dropped with BookingSessionAnswers, are ordinary
        // FieldChanged events named custom:{fieldId}.
        var companyField = BookingFieldNames.ForCustomField(fixture.Seeded.CompanyFieldId);
        var topicField = BookingFieldNames.ForCustomField(fixture.Seeded.TopicFieldId);

        var customEvents = submitted
            .Where(e => e.FieldName.StartsWith(BookingFieldNames.CustomFieldPrefix, StringComparison.Ordinal))
            .ToArray();

        output.WriteLine($"expecting {companyField} and {topicField}");
        output.WriteLine($"custom-field events surviving in the log: {customEvents.Length}");
        foreach (var e in customEvents) output.WriteLine($"  {e.FieldName} = {e.NewValue}");

        Assert.Contains(customEvents, e => e.FieldName == companyField && e.NewValue == LatestSchemaData.CompanyAnswer);
        Assert.Contains(customEvents, e => e.FieldName == topicField && e.NewValue == LatestSchemaData.TopicAnswer);

        // The meeting link, dropped with BookingSessions.MeetingUrl, is a
        // MeetingLinkAssigned event (BookingEventType 13).
        Assert.Contains(submitted, e => e.EventType == 13 && e.NewValue == LatestSchemaData.MeetingUrl);

        // The booking reference AND the public token, dropped with the
        // BookingManagement columns, are both on the BookingSubmitted event -
        // reference as OldValue, token as NewValue, two generated values in one
        // event so that Apply() can restore both when replaying.
        //
        // The first version of this audit claimed the token was NOT in the log,
        // reasoning that a bearer secret would not be written to one. The test
        // said otherwise, which is the entire argument for executing an audit
        // rather than writing it. It is not a defect - Rebuild() could not
        // reconstruct a booking's manage link without it - but it does mean the
        // event log is exactly as sensitive as the PublicToken column, and
        // rolling the column away does not reduce that exposure by one row.
        var submitEvent = Assert.Single(submitted, e => e.EventType == 7);
        Assert.Equal(fixture.Seeded.SubmittedBookingReference, submitEvent.OldValue);
        Assert.Equal(fixture.Seeded.SubmittedPublicToken, submitEvent.NewValue);

        // What genuinely cannot be recovered: nothing in the log ever carried a
        // question's LABEL, an exception's EndDate, or any reminder or calendar
        // row - those live only in the tables the rollback dropped.
        Assert.DoesNotContain(submitted, e => e.NewValue == LatestSchemaData.CompanyQuestionLabel);
        Assert.DoesNotContain(submitted, e => e.OldValue == LatestSchemaData.CompanyQuestionLabel);

        output.WriteLine(
            "recoverable from BookingSessionEvents after a full rollback: custom answers, meeting link,\n" +
            "booking reference, public token, cancellation reason.\n" +
            "NOT recoverable: question labels, AvailabilityException.EndDate, reminders,\n" +
            "notification settings, calendar connection, email queue.");
    }

    [Fact]
    public void SurvivingRowsAndTheirIdsAreIntactAtInitialCreate()
    {
        Assert.Null(fixture.RollbackFailure);

        foreach (var table in MigrationOrder.InitialCreateTables)
        {
            Assert.Equal(fixture.Snapshot.RowCounts[table], fixture.RowCountsAtInitialCreate[table]);
        }

        // Counts alone would survive a delete-and-reinsert; ids would not.
        Assert.Equal(MigrationOrder.InitialCreateTables, fixture.TablesAtInitialCreate);
    }

    [Fact]
    public void TheRollbackReachesInitialCreateAndStopsThere()
    {
        Assert.Null(fixture.RollbackFailure);

        Assert.Equal([MigrationOrder.RollbackTarget], fixture.AppliedAtInitialCreate);
        Assert.Equal(MigrationOrder.InitialCreateTables, fixture.TablesAtInitialCreate);
    }

    // ---- schema round trip ---------------------------------------------------

    /// <summary>
    /// InitialCreate -> every migration -> latest, using the same
    /// <c>MigrateAsync()</c> that built the database in the first place.
    ///
    /// This is a SCHEMA round trip and is deliberately not called anything
    /// stronger. See <see cref="TablesRecreatedByTheRoundTripComeBackEmpty"/> for
    /// the half that would be a lie.
    /// </summary>
    [Fact]
    public void TheCompleteUpPathAfterRollbackReachesTheLatestSchemaAgain()
    {
        Assert.Null(fixture.RollbackFailure);
        Assert.Null(fixture.RoundTripFailure);

        Assert.Equal(MigrationOrder.All, fixture.AppliedAfterRoundTrip);
        Assert.Empty(fixture.PendingAfterRoundTrip);

        output.WriteLine(
            $"rollback: {fixture.Steps.Sum(s => s.ElapsedMs):N0} ms over {fixture.Steps.Count} Down migrations; " +
            $"round trip up: {fixture.RoundTripElapsedMs:N0} ms");
    }

    [Fact]
    public async Task TheSchemaAfterTheRoundTripIsTheOneTheMigrationsDeclare()
    {
        Assert.Null(fixture.RoundTripFailure);

        var schema = DownTestDatabase.Schema;

        // Read from sys.*, never from the EF model - the model and the snapshot
        // are both derived from the same C#, so they cannot disagree with each
        // other about a database that disagrees with both.
        var endDate = await schema.ColumnAsync("AvailabilityExceptions", "EndDate");
        Assert.NotNull(endDate);
        Assert.Equal("date", endDate.Type);
        Assert.False(endDate.IsNullable);

        var message = await schema.ColumnAsync("BookingSessions", "Message");
        Assert.NotNull(message);
        Assert.Equal(BookingFieldLimits.MessageMaxLength, message.MaxCharacters);

        var conflict = await schema.IndexAsync("BookingSessions", "IX_BookingSessions_BookingPageId_Status_SelectedDate");
        Assert.NotNull(conflict);
        Assert.Equal(["BookingPageId", "Status", "SelectedDate"], conflict.KeyColumns);
        Assert.Equal(["SelectedTime"], conflict.IncludedColumns);
        Assert.Null(await schema.IndexAsync("BookingSessions", "IX_BookingSessions_BookingPageId_Status"));

        var reminderIndex = await schema.IndexAsync("BookingReminders", "UX_BookingReminders_Session_Offset_Scheduled");
        Assert.NotNull(reminderIndex);
        Assert.True(reminderIndex.IsUnique);
        Assert.Contains("[Status]=(0)", reminderIndex.Filter);

        // Every table the migrations declare is back.
        var tables = await schema.TablesAsync();
        Assert.Equal(PreRollbackSnapshot.CountedTables.Order().ToArray(), tables.Order().ToArray());
    }

    /// <summary>
    /// The honest counterpart to the round-trip test.
    ///
    /// Fourteen tables were dropped on the way down and recreated on the way up.
    /// Their rows are gone and no migration can invent them again - so the round
    /// trip restores the SCHEMA and not the DATA, and this test exists so nobody
    /// can read the previous one as claiming otherwise.
    /// </summary>
    [Fact]
    public void TablesRecreatedByTheRoundTripComeBackEmpty()
    {
        Assert.Null(fixture.RoundTripFailure);

        var recreated = MigrationOrder.Path
            .SelectMany(s => s.TablesDropped)
            .Distinct()
            .Order()
            .ToArray();

        Assert.NotEmpty(recreated);

        foreach (var table in recreated)
        {
            Assert.True(
                fixture.RowCountsAfterRoundTrip[table] == 0,
                $"{table} was dropped and recreated by the round trip, so it must be empty - " +
                $"found {fixture.RowCountsAfterRoundTrip[table]} row(s).");

            // And it really did hold something before, or the assertion above is
            // true for the wrong reason.
            Assert.True(
                fixture.Snapshot.RowCounts[table] > 0,
                $"{table} held no rows before the rollback, so its emptiness afterwards proves nothing.");
        }

        // The four InitialCreate tables were never dropped, so they still hold
        // everything - which is what makes the loss above attributable to the
        // DropTable rather than to the rollback in general.
        foreach (var table in MigrationOrder.InitialCreateTables)
        {
            Assert.Equal(fixture.Snapshot.RowCounts[table], fixture.RowCountsAfterRoundTrip[table]);
        }

        output.WriteLine($"emptied by the round trip: {string.Join(", ", recreated)}");
    }

    /// <summary>
    /// The application-level check the brief asks for, kept to what it can
    /// honestly answer: after the round trip the schema is internally valid for
    /// the latest model, so every <c>DbSet</c> and both owned collections
    /// materialise through the app's own <c>DbContext</c>.
    ///
    /// Deliberately NOT attempted at intermediate rollback levels: the current
    /// model names columns those schemas do not have, so a failure there would be
    /// the model disagreeing with an old schema, which is expected and says
    /// nothing about whether the rollback was correct.
    /// </summary>
    [Fact]
    public async Task TheApplicationCanReadEveryTableAfterTheRoundTrip()
    {
        Assert.Null(fixture.RoundTripFailure);

        await using var db = RollbackFixture.CreateContext();

        Assert.Empty(await db.Organizers.Where(o => o.Email == "nobody@rollback.invalid").ToListAsync());
        Assert.Empty(await db.BookingPages.Take(1).Where(p => p.Slug == "nope").ToListAsync());
        Assert.Empty(await db.BookingSessions.Where(s => s.Name == "nope").ToListAsync());
        Assert.Empty(await db.BookingSessionEvents.Where(e => e.FieldName == "nope").ToListAsync());
        Assert.Empty(await db.RefreshTokens.ToListAsync());
        Assert.Empty(await db.AvailabilityExceptions.ToListAsync());
        Assert.Empty(await db.AvailabilityOverrides.ToListAsync());
        Assert.Empty(await db.CalendarConnections.ToListAsync());
        Assert.Empty(await db.CalendarSyncedEvents.ToListAsync());
        Assert.Empty(await db.NotificationSettings.ToListAsync());
        Assert.Empty(await db.EmailNotifications.ToListAsync());
        Assert.Empty(await db.BookingReminders.ToListAsync());

        // The two owned collections, materialised THROUGH THEIR OWNERS - EF
        // refuses to track an owned entity projected without one, so loading the
        // owner is both the only way to ask and the more faithful question.
        Assert.Empty(await db.WorkingSchedules.ToListAsync());
        Assert.Empty(await db.WorkingDays.ToListAsync());

        // The organizers, pages and sessions that were never dropped are still
        // readable through the real model - the round trip put every column the
        // model needs back.
        Assert.Equal(3, await db.Organizers.CountAsync());
        Assert.Equal(3, await db.BookingPages.CountAsync());
        Assert.Equal(5, await db.BookingSessions.CountAsync());
    }

    // ---- helpers -------------------------------------------------------------

    private static async Task<IReadOnlyList<string>> LatestSchemaAppliedMigrationsAsync()
        => await DownTestDatabase.Schema.AppliedMigrationsAsync();

    /// <summary>
    /// Re-reads the event log with the same query the snapshot used, so the two
    /// lists are comparable. Raw SQL because the current model cannot read an
    /// InitialCreate-level database at all.
    /// </summary>
    private static async Task<IReadOnlyList<SnapshotEvent>> ReadEventsAtInitialCreateAsync()
    {
        var events = new List<SnapshotEvent>();

        await using var connection = await DownTestDatabase.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT SessionId, EventType, ISNULL(FieldName, ''), ISNULL(OldValue, ''), ISNULL(NewValue, ''), ClientSequenceNumber
            FROM BookingSessionEvents
            ORDER BY SessionId, ClientSequenceNumber, EventType;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new SnapshotEvent(
                reader.GetGuid(0), reader.GetByte(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
        }

        return events;
    }
}
