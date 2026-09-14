using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Infrastructure.BackgroundServices;

/// <summary>
/// Safety net for the cases the frontend's beacon-on-unload can't catch
/// (crashed tab, lost network, phone locked). Sessions left Active with no
/// activity for longer than the threshold are marked Abandoned and get a
/// synthetic BookingAbandoned event appended to their log.
/// </summary>
public class BookingSessionAbandonmentSweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingSessionAbandonmentSweeper> _logger;

    public BookingSessionAbandonmentSweeper(IServiceScopeFactory scopeFactory, ILogger<BookingSessionAbandonmentSweeper> logger)
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
                _logger.LogError(ex, "Booking session abandonment sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IBookingTrackerDbContext>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<IEventBroadcaster>();

        var cutoff = DateTime.UtcNow - InactivityThreshold;
        var staleSessions = await db.BookingSessions
            .Where(s => s.Status == BookingSessionStatus.Active && s.LastActivityAt < cutoff)
            .ToListAsync(cancellationToken);

        if (staleSessions.Count == 0) return;

        var newEvents = new List<BookingSessionEvent>();
        foreach (var session in staleSessions)
        {
            var @event = session.Abandon();
            if (@event is not null) newEvents.Add(@event);
        }

        db.BookingSessionEvents.AddRange(newEvents);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var @event in newEvents)
        {
            await broadcaster.BroadcastEventAsync(@event.ToDto(), cancellationToken);
        }
        foreach (var session in staleSessions)
        {
            await broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);
        }

        _logger.LogInformation("Marked {Count} booking session(s) as abandoned.", newEvents.Count);
    }
}
