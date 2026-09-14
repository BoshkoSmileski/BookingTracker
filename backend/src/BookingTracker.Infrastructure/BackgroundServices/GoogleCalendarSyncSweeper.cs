using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Infrastructure.BackgroundServices;

/// <summary>
/// Keeps calendar connections healthy without organizer intervention: refreshes access tokens
/// before they expire, periodically re-verifies the selected calendar still exists and
/// permissions haven't been revoked, and retries connections that failed last time in case the
/// underlying problem (rate limit, outage) has since cleared. Reuses
/// ICalendarConnectionService.SyncNowAsync - the exact same path the organizer's own "Sync Now"
/// button calls - so there is only one place that logic lives.
/// </summary>
public class GoogleCalendarSyncSweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    /// <summary>Refresh proactively this far ahead of real token expiry.</summary>
    private static readonly TimeSpan RefreshLookahead = TimeSpan.FromMinutes(15);

    /// <summary>Even a healthy connection gets a full health check at least this often.</summary>
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleCalendarSyncSweeper> _logger;

    public GoogleCalendarSyncSweeper(IServiceScopeFactory scopeFactory, ILogger<GoogleCalendarSyncSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Calendar sync sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IBookingTrackerDbContext>();
        var connectionService = scope.ServiceProvider.GetRequiredService<ICalendarConnectionService>();

        var now = DateTime.UtcNow;
        var refreshCutoff = now.Add(RefreshLookahead);
        var healthCheckCutoff = now - HealthCheckInterval;

        // Only touch connections that actually need attention this cycle - unconditionally
        // re-syncing every connected organizer on every sweep would be wasteful and risks
        // tripping Google's rate limits for no benefit.
        var candidates = await db.CalendarConnections
            .AsNoTracking()
            .Where(c =>
                c.AccessTokenExpiresAtUtc <= refreshCutoff
                || c.Status != CalendarSyncStatus.Connected
                || c.LastSuccessfulSyncAtUtc == null
                || c.LastSuccessfulSyncAtUtc < healthCheckCutoff)
            .Select(c => c.OrganizerId)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        _logger.LogInformation("Google Calendar sync sweep: checking {Count} connection(s).", candidates.Count);

        var succeeded = 0;
        var failed = 0;
        foreach (var organizerId in candidates)
        {
            try
            {
                var result = await connectionService.SyncNowAsync(organizerId, cancellationToken);
                if (result.Status == nameof(CalendarSyncStatus.Connected)) succeeded++;
                else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Google Calendar sync sweep failed for organizer {OrganizerId}.", organizerId);
            }
        }

        _logger.LogInformation("Google Calendar sync sweep finished: {Succeeded} succeeded, {Failed} failed.", succeeded, failed);
    }
}
