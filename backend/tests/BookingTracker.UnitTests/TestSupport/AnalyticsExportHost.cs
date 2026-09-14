using BookingTracker.Application;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// A real MediatR pipeline over an InMemory database.
///
/// The export handlers compose the dashboard by *sending* the four analytics
/// queries, so a hand-written ISender that returned canned DTOs would test
/// nothing: the whole claim being verified is that an export goes through the
/// same handlers the dashboard does. Registering the actual Application assembly
/// (AddApplication, exactly as Program.cs does) makes that dispatch real, which
/// is why this is a container rather than a fake - the same reasoning as using a
/// real InMemory DbContext instead of a mocked DbSet.
/// </summary>
public sealed class AnalyticsExportHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private AnalyticsExportHost(BookingTrackerDbContext db, ServiceProvider provider)
    {
        Db = db;
        _provider = provider;
        Sender = provider.GetRequiredService<ISender>();
    }

    public BookingTrackerDbContext Db { get; }

    public ISender Sender { get; }

    public static AnalyticsExportHost Create()
    {
        var db = InMemoryDbContextFactory.Create();

        var services = new ServiceCollection();
        // MediatR 14 resolves an ILoggerFactory during registration; the web host
        // supplies one automatically, a bare ServiceCollection does not.
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IBookingTrackerDbContext>(db);

        return new AnalyticsExportHost(db, services.BuildServiceProvider());
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await Db.DisposeAsync();
    }
}
