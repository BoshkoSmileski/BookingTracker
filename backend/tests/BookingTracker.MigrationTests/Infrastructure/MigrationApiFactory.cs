using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// The integration host, pointed at the database this suite just upgraded.
///
/// It exists so the post-migration checks can be made through the REAL
/// application rather than only through <c>sys.columns</c>: a schema that
/// matches the model on paper and an application that can actually read and
/// write the migrated rows are two different claims, and only the second one is
/// what a developer cares about after an upgrade.
///
/// Everything except the database is inherited, exactly as
/// <c>SqlServerApiFactory</c> does it - no sweepers, a recording email sender
/// (nothing is transmitted and nothing is written to %TEMP%), a recording
/// calendar service (no Google call is reachable), redirected Data Protection
/// keys, and an environment of "Testing" rather than "Development" so
/// <c>Program.cs</c> neither migrates nor seeds underneath the tests. The last
/// of those matters more here than anywhere else: the Development branch would
/// run <c>MigrateAsync</c> itself, which is the one thing this suite has to be
/// the only caller of.
/// </summary>
public sealed class MigrationApiFactory : BookingTrackerApiFactory
{
    protected override void ConfigureDatabase(IServiceCollection services)
    {
        MigrationTestDatabase.AssertIsolated();

        RemoveDatabaseRegistration(services);

        services.AddDbContext<BookingTrackerDbContext>(options =>
            options.UseSqlServer(MigrationTestDatabase.ConnectionString));

        services.AddScoped<IBookingTrackerDbContext>(sp => sp.GetRequiredService<BookingTrackerDbContext>());
    }
}

/// <summary>Base for the post-migration tests: the shared integration base, on the migrated database.</summary>
[Collection(MigrationCollection.Name)]
public abstract class MigrationApiTestBase : ApiTestBase
{
    protected override BookingTrackerApiFactory CreateFactory() => new MigrationApiFactory();
}
