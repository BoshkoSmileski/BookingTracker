using BookingTracker.Application.Common.Interfaces;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.SqlServerTests.Infrastructure;

/// <summary>
/// The InMemory integration host with one thing changed: a real SQL Server
/// database.
///
/// Everything else is inherited deliberately - no sweepers (so a queue row that
/// appears was queued by the request, not drained by a timer), a recording
/// email sender (so nothing is transmitted and nothing is written to %TEMP%), a
/// recording calendar service (so no Google call is reachable), redirected Data
/// Protection keys, and an environment of "Testing" rather than "Development"
/// so <c>Program.cs</c> neither migrates nor seeds underneath the tests.
///
/// The connection string is supplied HERE, in the service registration, and
/// never as an environment variable. The base host's static constructor points
/// <c>ConnectionStrings__DefaultConnection</c> at a deliberately unreachable
/// server, and that stays true: if this override ever failed to take effect,
/// the result is a connection error, not a test quietly writing to the
/// developer's database.
/// </summary>
public sealed class SqlServerApiFactory : BookingTrackerApiFactory
{
    protected override void ConfigureDatabase(IServiceCollection services)
    {
        SqlServerTestDatabase.AssertIsolated();

        RemoveDatabaseRegistration(services);

        // No EnableRetryOnFailure, matching production exactly. Adding a
        // retrying execution strategy here would change what
        // ExecuteInTransactionAsync does under contention, which is the single
        // behaviour this project exists to observe.
        services.AddDbContext<BookingTrackerDbContext>(options =>
            options.UseSqlServer(SqlServerTestDatabase.ConnectionString));

        services.AddScoped<IBookingTrackerDbContext>(sp => sp.GetRequiredService<BookingTrackerDbContext>());
    }
}
