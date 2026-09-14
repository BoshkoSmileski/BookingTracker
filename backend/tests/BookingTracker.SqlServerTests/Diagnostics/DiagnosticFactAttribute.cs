namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// A test that MEASURES rather than asserts, and is therefore opt-in.
///
/// Everything under Diagnostics/ exists to produce evidence about SQL Server's
/// behaviour - execution plans, logical reads, lock footprints, contention
/// latency. Those are inputs to a decision, not regressions to guard, and they
/// are slow: seeding a dataset big enough for a scan to be measurable takes far
/// longer than the whole rest of this suite.
///
/// So they are skipped unless <see cref="EnableVariable"/> is set. The suite
/// stays the same size in CI and in a normal <c>dotnet test</c>, and the
/// measurements are reproducible on demand:
///
/// <code>
/// $env:BOOKINGTRACKER_RUN_DIAGNOSTICS = "1"
/// dotnet test tests/BookingTracker.SqlServerTests -c Release --filter "FullyQualifiedName~Diagnostics"
/// </code>
///
/// This is deliberately NOT how the correctness tests are gated. A test that can
/// fail belongs in the suite unconditionally - see SlotConflictConcurrencyTests,
/// which stays exactly as it was.
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
