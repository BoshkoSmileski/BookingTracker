using System.Text.RegularExpressions;
using BookingTracker.Application.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// The conflict query, in a form SQL Server can be asked about directly.
///
/// <see cref="BookingConflictChecker.EnsureSlotIsAvailableAsync"/> runs its query
/// through EF, which is the right thing for production and the wrong thing for a
/// measurement: STATISTICS XML, STATISTICS IO and sys.dm_tran_locks all need the
/// statement to be issued on a connection this code controls.
///
/// So the LINQ is mirrored here and turned into executable T-SQL with
/// ToQueryString(). A mirror is a second copy, and a second copy drifts - which
/// is what <see cref="AssertMatchesProductionAsync"/> is for: it runs the real
/// checker against the same database, captures the SQL EF actually sent, and
/// fails if the mirror no longer produces the same statement. A measurement of
/// the wrong query is worse than no measurement, because it still produces
/// numbers.
/// </summary>
public static class ConflictQuery
{
    /// <summary>
    /// Executable T-SQL for "which of this organizer's bookings are on this date",
    /// parameters inlined as DECLAREs exactly as EF would bind them.
    /// </summary>
    public static string Sql(Guid organizerId, Guid sessionIdToExclude, DateOnly date)
    {
        using var db = SqlServerDatabaseFixture.CreateContext();

        // Character for character the query in BookingConflictChecker
        // .EnsureSlotIsAvailableAsync, with page.OrganizerId supplied directly
        // instead of read from a page loaded a moment earlier. The anonymous
        // type is part of that: EF aliases its columns after the property names,
        // so projecting into a record instead produces different SQL - which the
        // drift guard below caught the first time this was written that way.
        var query =
            from other in db.BookingSessions.AsNoTracking()
            join otherPage in db.BookingPages.AsNoTracking() on other.BookingPageId equals otherPage.Id
            where otherPage.OrganizerId == organizerId
                && other.Id != sessionIdToExclude
                && other.Status == BookingSessionStatus.Submitted
                && other.SelectedDate == date
            select new { Time = other.SelectedTime!.Value, otherPage.DurationMinutes, otherPage.BufferBeforeMinutes, otherPage.BufferAfterMinutes };

        return query.ToQueryString();
    }

    /// <summary>
    /// The claim GATE - the statement added ahead of the conflict
    /// check, rendered exactly as production issues it.
    ///
    /// <c>ToQueryString()</c> renders a query without executing it, so no
    /// command interceptor runs and the update-lock hint is absent. Applying the
    /// production rewriter here is what keeps the mirror faithful - and because
    /// <see cref="AssertGateMatchesProductionAsync"/> compares this against the
    /// SQL EF really sent, it is also what proves the interceptor is wired up at
    /// all: unhook it and the two stop matching.
    /// </summary>
    public static string GateSql(Guid bookingPageId, bool withClaimLock = true)
    {
        using var db = SqlServerDatabaseFixture.CreateContext();

        // Take(1) rather than FirstOrDefault, which ToQueryString cannot render -
        // both produce SELECT TOP(1), so the statement measured is the statement
        // issued.
        var sql = db.Organizers
            .Where(o => db.BookingPages.Any(p => p.Id == bookingPageId && p.OrganizerId == o.Id))
            .Select(o => o.Id)
            .TagWith(BookingConflictChecker.ClaimGateTag)
            .Take(1)
            .ToQueryString();

        return withClaimLock ? ClaimLockInterceptor.AddUpdateLockHint(sql) : sql;
    }

    /// <summary>
    /// The gate's counterpart to <see cref="AssertMatchesProductionAsync"/>, and
    /// the one assertion that proves ClaimLockInterceptor is actually reached:
    /// it drives the real <c>ClaimSlotAsync</c> with a claim that does nothing,
    /// captures what EF sent, and compares the tagged statement to the mirror.
    /// If the interceptor is unregistered, the captured SQL loses its
    /// <c>WITH (UPDLOCK)</c> and this fails.
    /// </summary>
    public static async Task AssertGateMatchesProductionAsync(
        Guid pageId, DateOnly date, TimeOnly time, Action<string> log)
    {
        var statements = new List<string>();
        await using var db = Recording(statements);

        try
        {
            await BookingConflictChecker.ClaimSlotAsync(
                db, Guid.NewGuid(), pageId, date, time, () => Task.CompletedTask, CancellationToken.None);
        }
        catch (Application.Common.Exceptions.ConflictException)
        {
            // The gate runs before the conflict check, so whether the probe slot
            // is taken makes no difference to what was captured.
        }

        var production = statements
            .Select(ExtractSelect)
            .LastOrDefault(sql => sql.Contains("[Organizers]", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The claim issued no query against Organizers, so the gate never ran. Captured:\n"
                + string.Join("\n---\n", statements));

        Assert.Contains("WITH (UPDLOCK)", production, StringComparison.Ordinal);
        Assert.Equal(Normalise(ExtractSelect(GateSql(pageId))), Normalise(production));

        log("Claim gate verified against the SQL ClaimSlotAsync actually issued, update lock included.");
    }

    /// <summary>
    /// Proves the mirror is still the production query.
    ///
    /// Note what is compared: the SELECT text, with parameter NAMES normalised
    /// away. EF names parameters after the closure variables they came from
    /// (<c>@__page_OrganizerId_0</c> in the checker, <c>@__organizerId_0</c>
    /// here), which is a difference in this file rather than a difference in the
    /// query - so comparing them raw would fail for a reason that means nothing.
    /// Everything that decides an access path - the tables, the joins, the
    /// predicates, the projected columns - is compared exactly.
    /// </summary>
    public static async Task AssertMatchesProductionAsync(
        Guid organizerId, Guid pageId, Guid sessionIdToExclude, DateOnly date, TimeOnly time, Action<string> log)
    {
        var statements = new List<string>();
        await using var db = Recording(statements);

        try
        {
            await BookingConflictChecker.EnsureSlotIsAvailableAsync(
                db, sessionIdToExclude, pageId, date, time, CancellationToken.None);
        }
        catch (Application.Common.Exceptions.ConflictException)
        {
            // Whether the probe slot happens to be taken is irrelevant here - the
            // query has already run either way, which is the whole point.
        }

        var production = statements
            .Select(ExtractSelect)
            .LastOrDefault(sql => sql.Contains("[BookingSessions]", StringComparison.Ordinal)
                                  && sql.Contains("[BookingPages]", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The checker issued no query touching both BookingSessions and BookingPages. " +
                "Captured:\n" + string.Join("\n---\n", statements));

        var mirrored = ExtractSelect(Sql(organizerId, sessionIdToExclude, date));

        Assert.Equal(Normalise(production), Normalise(mirrored));
        log("Mirror verified against the SQL BookingConflictChecker actually issued.");
    }

    /// <summary>A real context against the test database that records every statement it sends.</summary>
    private static BookingTrackerDbContext Recording(List<string> statements) => new(
        new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseSqlServer(SqlServerTestDatabase.ConnectionString)
            .EnableSensitiveDataLogging()
            .LogTo(statements.Add, [RelationalEventId.CommandExecuted], LogLevel.Information, DbContextLoggerOptions.None)
            .Options);

    /// <summary>The SELECT, dropping any DECLARE prelude and EF's own log header line.</summary>
    private static string ExtractSelect(string sql)
    {
        var index = sql.IndexOf("SELECT", StringComparison.Ordinal);
        return index < 0 ? sql : sql[index..];
    }

    /// <summary>
    /// Collapses whitespace, erases parameter names, and reduces a row limit to
    /// the word TOP - all three differ by call site rather than by query. EF
    /// names parameters after the closure variables they came from, and renders
    /// the limit as a literal for <c>FirstOrDefault</c> but as a parameter for
    /// the <c>Take(1)</c> a mirror has to use, neither of which changes an
    /// access path. Everything that does - tables, joins, predicates, projected
    /// columns, and the locking hint - is compared exactly.
    /// </summary>
    private static string Normalise(string sql)
    {
        var normalised = Regex.Replace(sql, @"@__\w+", "@p");
        normalised = Regex.Replace(normalised, @"TOP\(\S+?\)", "TOP");
        return Regex.Replace(normalised, @"\s+", " ").Trim();
    }
}
