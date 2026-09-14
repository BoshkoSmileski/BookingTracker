using System.Data.Common;
using System.Text.RegularExpressions;
using BookingTracker.Application.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookingTracker.Infrastructure.Persistence;

/// <summary>
/// Gives the slot claim's gate read (see
/// <c>BookingConflictChecker.ClaimGateTag</c>) an UPDATE lock, which is what
/// makes two guests claiming against the same organizer take turns.
///
/// The protocol this produces: the first claimer takes <c>U</c> on that
/// organizer's row and carries on; the second blocks there, before it has read
/// or locked anything else; the first commits and releases; the second reads,
/// now sees the booking that was just made, and is refused with the 409 that
/// was always the intended answer.
///
/// What it replaces is a conversion deadlock, confirmed from SQL Server's own
/// deadlock graph rather than inferred: every racer held <c>RangeS-S</c> on one
/// key of IX_BookingSessions_BookingPageId_Status_SelectedDate and then
/// requested <c>RangeI-N</c> on it as a <c>requestType="convert"</c>, because
/// moving its own session from Active to Submitted inserts a key into the range
/// the others were holding shared. Nobody could be granted, so SQL Server chose
/// a victim - a correct outcome reached through the deadlock monitor's ~5s scan
/// rather than through a lock wait.
///
/// Three things about the shape of this fix are deliberate:
///
///  - <b>The gate, not the conflict read.</b> Locking the range the conflict
///    check scans was tried and measured first, and it fails above four racers:
///    which keys that seek must lock depends on which bookings exist, so
///    waiters resume wanting a different key than they queued on and deadlock
///    over the ordering instead. One row that always exists has no ordering.
///  - <b>UPDLOCK, not XLOCK.</b> U conflicts with U and X only, so every
///    ordinary reader of Organizers - login, the dashboard, email rendering -
///    is unaffected.
///  - <b>No HOLDLOCK.</b> The caller is already inside a Serializable
///    transaction, which is what holds this lock through to the commit.
/// </summary>
public sealed class ClaimLockInterceptor : DbCommandInterceptor
{
    /// <summary>
    /// EF renders <c>TagWith</c> as a leading comment, so the tag arrives here
    /// inside the command text itself - which is the whole reason the intent can
    /// be declared in Application without Application containing any T-SQL.
    /// </summary>
    private static readonly Regex GateSource = new(
        @"\bFROM \[Organizers\] AS \[(?<alias>\w+)\]",
        RegexOptions.Compiled);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ApplyClaimLock(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ApplyClaimLock(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static void ApplyClaimLock(DbCommand command)
    {
        if (!command.CommandText.Contains(BookingConflictChecker.ClaimGateTag, StringComparison.Ordinal)) return;

        command.CommandText = AddUpdateLockHint(command.CommandText);
    }

    /// <summary>
    /// Rewrites the tagged statement's Organizers source to take an update lock.
    /// Public so the SQL Server test suite can build the same statement it
    /// measures, which is how "the hint is really applied in production" stays a
    /// verified claim rather than an assumed one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If the statement does not read Organizers exactly once. Returning it
    /// unhinted instead would be worse than failing: bookings would still be
    /// correct and would quietly go back to costing a deadlock apiece, which is
    /// exactly the failure this class removes and exactly the kind nobody
    /// notices. The gate query's shape can only change by someone editing it, so
    /// this throw is reached in BookingTracker.SqlServerTests, not in front of a
    /// guest.
    /// </exception>
    public static string AddUpdateLockHint(string sql)
    {
        var matches = GateSource.Matches(sql);

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"The slot-claim gate was expected to select FROM [Organizers] exactly once so its update " +
                $"lock could be applied, but the generated SQL does that {matches.Count} time(s). " +
                $"BookingConflictChecker.AcquireClaimGateAsync has changed shape - update " +
                $"ClaimLockInterceptor to match it. SQL:{Environment.NewLine}{sql}");
        }

        return GateSource.Replace(sql, "$0 WITH (UPDLOCK)", count: 1);
    }
}
