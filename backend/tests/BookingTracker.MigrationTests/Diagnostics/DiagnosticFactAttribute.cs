namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// A test that MEASURES rather than asserts, and is therefore opt-in.
///
/// Everything under Diagnostics/ in this assembly exists to produce evidence
/// about what the migrations do to a database that already holds a realistic
/// number of rows - elapsed time, logical reads, writes, lock footprint, whether
/// a concurrent application read is blocked. Those are inputs to a deployment
/// decision, not regressions to guard, and they are slow: seeding half a million
/// booking sessions and then migrating over them takes minutes, where the whole
/// twelve-test correctness suite beside it takes seconds.
///
/// So they are skipped unless <see cref="EnableVariable"/> is set:
///
/// <code>
/// $env:BOOKINGTRACKER_RUN_DIAGNOSTICS = "1"
/// dotnet test tests/BookingTracker.MigrationTests -c Release --filter "FullyQualifiedName~Diagnostics"
/// </code>
///
/// **The variable name is deliberately identical to the one
/// <c>BookingTracker.SqlServerTests.Diagnostics.DiagnosticFactAttribute</c>
/// uses**, so one switch runs every diagnostic in the repository and the
/// convention is preserved exactly. The fifteen lines are duplicated rather than
/// shared, because sharing them would mean a ProjectReference from this suite to
/// that one - and the two SQL-backed suites are deliberately independent (a
/// break in one must not turn the other's build red; see the CI job comments).
/// A build-time dependency between peer suites is a worse trade than one small
/// attribute, and the thing that actually has to stay in step - the variable
/// name - is a string that would have to match either way.
///
/// This is deliberately NOT how the correctness tests in this assembly are
/// gated. A test that can fail belongs in the suite unconditionally - see
/// PopulatedDatabaseMigrationTests and PostMigrationApplicationTests, which stay
/// exactly as they were.
/// </summary>
public sealed class DiagnosticFactAttribute : FactAttribute
{
    public const string EnableVariable = "BOOKINGTRACKER_RUN_DIAGNOSTICS";

    public DiagnosticFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnableVariable)))
        {
            Skip = $"Diagnostic measurement. Set {EnableVariable}=1 to run it.";
        }
    }
}
