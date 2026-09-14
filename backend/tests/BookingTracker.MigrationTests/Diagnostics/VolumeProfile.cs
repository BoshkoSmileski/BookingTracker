namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// How much data a volume level puts in the database, and why each number is
/// what it is.
///
/// The point of these sizes is not realism for its own sake. The correctness suite proved
/// migration correctness on roughly fifty rows, which is exactly the size at
/// which every migration is instant and every lock is held for a millisecond -
/// so a measurement taken there cannot support or refute a claim about
/// deployment safety. What matters is that the three levels differ by an order
/// of magnitude each, so the OUTPUT SHOWS A SLOPE rather than a single number
/// that could mean anything.
///
/// <b>BookingSessions is the volume driver</b>, because it is the table the two
/// migrations under investigation actually touch at scale - and the one a busy
/// installation grows without bound, since every abandoned wizard visit is a row.
///
/// <b>AvailabilityExceptions is scaled separately and deliberately
/// pessimistically.</b> <c>AddAvailabilityExceptionEndDate</c>'s unfiltered
/// UPDATE only matters relative to THAT table's size, and no real organizer has
/// a thousand blocked periods. One exception per five sessions is far more than
/// any real installation would hold, which makes every number reported for that
/// migration an UPPER BOUND rather than an estimate - the useful direction to be
/// wrong in when the question is "is this safe to deploy".
///
/// <b>Organizers and BookingPages are scaled realistically instead</b>, at
/// roughly one organizer per five thousand sessions and a handful of pages each.
/// Inflating them would make the conflict index's selectivity better than a real
/// installation's, which is the wrong direction to be wrong in for the index
/// build.
/// </summary>
public sealed record VolumeProfile(string Label, int Sessions, int Exceptions, int Organizers, int PagesPerOrganizer)
{
    public int Pages => Organizers * PagesPerOrganizer;

    /// <summary>Roughly 10,000 booking sessions. Small enough that the whole level runs in well under a minute.</summary>
    public static readonly VolumeProfile Small =
        new("SMALL", Sessions: 10_000, Exceptions: 2_000, Organizers: 8, PagesPerOrganizer: 4);

    /// <summary>Roughly 100,000 booking sessions. A genuinely busy installation.</summary>
    public static readonly VolumeProfile Medium =
        new("MEDIUM", Sessions: 100_000, Exceptions: 20_000, Organizers: 40, PagesPerOrganizer: 4);

    /// <summary>
    /// Roughly 500,000 booking sessions - past anything this product is likely
    /// to see, which is the point: if the migrations are safe here they are safe
    /// at every size a thesis-scale deployment will reach.
    ///
    /// This is the level most likely to be impractical on a given machine. It is
    /// its own test rather than a third loop inside one, so it can be excluded
    /// with a filter without losing the other two.
    /// </summary>
    public static readonly VolumeProfile Large =
        new("LARGE", Sessions: 500_000, Exceptions: 100_000, Organizers: 100, PagesPerOrganizer: 4);

    public override string ToString()
        => $"{Label}: {Sessions:N0} sessions, {Exceptions:N0} availability exceptions, " +
           $"{Pages:N0} booking pages, {Organizers:N0} organizers";
}
