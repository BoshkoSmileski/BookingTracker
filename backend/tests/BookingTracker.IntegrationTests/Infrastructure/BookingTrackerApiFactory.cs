using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API in-process: real Program.cs, real middleware order, real
/// model binding, real controllers, real MediatR pipeline, real EF queries.
/// Only the four things that would reach outside the test process are replaced.
///
/// One factory per test class (via <see cref="ApiTestBase"/>), which is what
/// gives each class its own database AND its own rate-limiter state - the
/// limiters partition by client IP, and every TestServer request shares one, so
/// a single shared factory would eventually 429 tests that did nothing wrong.
/// </summary>
public class BookingTrackerApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Unique per factory, so parallel test classes never see each other's rows.</summary>
    private readonly string _databaseName = $"integration-{Guid.NewGuid()}";

    private readonly string _dataProtectionKeyDirectory =
        Path.Combine(Path.GetTempPath(), "BookingTracker.IntegrationTests", Guid.NewGuid().ToString());

    /// <summary>Every email the app "sent" during a test. Nothing leaves the process.</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Stands in for Google. No HTTP call to Google is possible from these tests.</summary>
    public RecordingCalendarSyncService Calendar { get; } = new();

    /// <summary>
    /// Test configuration is supplied as ENVIRONMENT VARIABLES, set once before
    /// any host is created, rather than through ConfigureAppConfiguration.
    ///
    /// This is not a style choice. Under minimal hosting, Program.cs reads
    /// `builder.Configuration` while its top-level statements run - notably the
    /// Jwt:Secret fail-fast - which happens BEFORE WebApplicationFactory gets to
    /// apply a ConfigureAppConfiguration callback. Environment variables are
    /// read by WebApplication.CreateBuilder itself, so they are in place in time.
    ///
    /// `__` is IConfiguration's cross-platform section separator, so these are
    /// exactly the keys the app already documents for production
    /// (JWT__Secret) - the same mechanism, pointed at test values.
    /// </summary>
    static BookingTrackerApiFactory()
    {
        var settings = new Dictionary<string, string>
        {
            // Program.cs fails fast without these, by design. Obviously not a
            // real secret, and it exists only inside the test process.
            ["Jwt__Issuer"] = TestJwt.Issuer,
            ["Jwt__Audience"] = TestJwt.Audience,
            ["Jwt__Secret"] = TestJwt.Secret,
            ["Jwt__AccessTokenMinutes"] = "15",
            ["Jwt__RefreshTokenDays"] = "30",

            // Read by AddInfrastructure before the registration is replaced in
            // ConfigureServices. Deliberately not a reachable server, so a
            // failure to override it surfaces as a connection error rather than
            // as a test quietly writing to somebody's database.
            ["ConnectionStrings__DefaultConnection"] = "Server=(integration-tests-never-connect);Database=none;",

            // False keeps EnsureValidForSending happy and selects the file sink -
            // which is itself then replaced, so not even a file is written.
            ["Email__UseSmtp"] = "false",
            ["Email__FromEmail"] = "tests@bookingtracker.invalid",
            ["Email__FromName"] = "BookingTracker Tests",

            // The real limits are 5/min on auth and 20/min on public-token,
            // partitioned per client IP - and every TestServer request shares
            // one IP. A focused suite trips that in seconds, so the two
            // configurable limits are raised HERE rather than the policies being
            // removed: the limiter middleware still runs on the real path, and
            // RateLimitingContractTests still pins its 429 rejection shape.
            ["RateLimiting__Authentication__PermitLimit"] = "10000",
            ["RateLimiting__Booking__PermitLimit"] = "10000",

            ["Frontend__BaseUrl"] = "https://tests.bookingtracker.invalid",
            ["Cors__AllowedOrigins__0"] = "https://tests.bookingtracker.invalid",

            // Left empty on purpose: any code path that genuinely tried to reach
            // Google would fail loudly rather than quietly appear to work.
            ["GoogleCalendar__ClientId"] = "",
            ["GoogleCalendar__ClientSecret"] = "",
            ["GoogleCalendar__RedirectUri"] = "https://tests.bookingtracker.invalid/api/calendar/google/callback",
        };

        foreach (var (key, value) in settings) Environment.SetEnvironmentVariable(key, value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // NOT "Development": that branch of Program.cs runs Database.MigrateAsync()
        // (which the InMemory provider cannot do) and then DevelopmentSeeder, which
        // would invent an organizer and a booking page underneath every test.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            ConfigureDatabase(services);
            RemoveBackgroundServices(services);

            // The two seams that reach the outside world. Replaced rather than
            // configured, so there is no setting anywhere that could turn a real
            // send or a real Google call back on inside a test.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<ICalendarSyncService>();
            services.AddSingleton<ICalendarSyncService>(Calendar);

            // AddInfrastructure points these at a shared %TEMP%\BookingTracker
            // folder that the developer's real app also uses. Redirected so a
            // test run cannot touch it.
            services.AddDataProtection()
                .PersistKeysToFileSystem(Directory.CreateDirectory(_dataProtectionKeyDirectory));
        });
    }

    /// <summary>
    /// Strips the SQL Server registration <c>AddInfrastructure</c> made, so a
    /// subclass can put something else in its place. Both the options and the
    /// context registration have to go: leaving DbContextOptions&lt;T&gt;
    /// behind leaves the SQL Server provider configured underneath the new
    /// registration.
    /// </summary>
    protected static void RemoveDatabaseRegistration(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<BookingTrackerDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<BookingTrackerDbContext>();
    }

    /// <summary>
    /// Swaps SQL Server for the InMemory provider the rest of this repository's
    /// tests already use.
    ///
    /// Virtual for exactly one caller: BookingTracker.SqlServerTests, which puts
    /// a real SQL Server database here instead, precisely to cover what the
    /// InMemory provider cannot honour (see the caveat below). Everything else
    /// about the host - the removed sweepers, the recording email and calendar
    /// doubles, the redirected Data Protection keys, the environment variables -
    /// is shared rather than written twice.
    /// </summary>
    protected virtual void ConfigureDatabase(IServiceCollection services)
    {
        RemoveDatabaseRegistration(services);

        services.AddDbContext<BookingTrackerDbContext>(options => options
            .UseInMemoryDatabase(_databaseName)
            // SubmitBookingSession wraps its work in ExecuteInTransactionAsync,
            // which the InMemory provider cannot honour and escalates to an
            // exception by default. Downgraded exactly as
            // InMemoryDbContextFactory.CreateIgnoringTransactions does in the
            // unit tests - and with the same caveat: nothing about
            // race-condition safety may be claimed from these tests, because
            // there is no real isolation here. The slot-conflict tests below
            // assert the SEQUENTIAL contract (second booker is refused), never
            // a concurrent one.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<IBookingTrackerDbContext>(sp => sp.GetRequiredService<BookingTrackerDbContext>());
    }

    /// <summary>
    /// Drops all four sweepers. They would otherwise run on their own timers
    /// mid-test: EmailQueueProcessor would drain the very queue these tests
    /// inspect, the abandonment sweeper would restate session status underneath
    /// assertions, and GoogleCalendarSyncSweeper would try to reach Google.
    /// Removing them is also what makes "was this queued?" answerable at all -
    /// the row stays Pending because nothing is draining it.
    /// </summary>
    private static void RemoveBackgroundServices(IServiceCollection services)
    {
        foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
        {
            services.Remove(hosted);
        }
    }

    /// <summary>
    /// Runs work against the app's own database, in its own scope - for seeding
    /// a test's starting state and for asserting on what an HTTP request
    /// actually persisted.
    /// </summary>
    public async Task<T> WithDbAsync<T>(Func<BookingTrackerDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<BookingTrackerDbContext>());
    }

    public async Task WithDbAsync(Func<BookingTrackerDbContext, Task> work)
        => await WithDbAsync(async db => { await work(db); return 0; });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try
        {
            if (Directory.Exists(_dataProtectionKeyDirectory)) Directory.Delete(_dataProtectionKeyDirectory, recursive: true);
        }
        catch
        {
            // A leftover temp key directory is not worth failing a test run over.
        }
    }
}
