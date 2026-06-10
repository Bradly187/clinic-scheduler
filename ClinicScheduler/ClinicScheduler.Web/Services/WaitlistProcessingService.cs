using ClinicScheduler.Core.Services;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Background service that sweeps the waitlist every 15 minutes: expires stale
/// entries and books open slots for matching ones, notifying patients on success.
/// The cancellation fast-path in AppointmentsController handles just-freed slots
/// immediately; this sweep catches everything else.
/// </summary>
public sealed class WaitlistProcessingService(
    IServiceScopeFactory scopeFactory,
    ILogger<WaitlistProcessingService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger startup so migrations/seeding finish first
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing waitlist");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var waitlist = scope.ServiceProvider.GetRequiredService<WaitlistService>();
        var notifier = scope.ServiceProvider.GetRequiredService<WaitlistFulfillmentNotifier>();

        var fulfillments = await waitlist.ProcessWaitlistAsync(ct);
        if (fulfillments.Count > 0)
        {
            await notifier.NotifyAsync(fulfillments, ct);
            logger.LogInformation("Waitlist sweep booked {Count} appointment(s)", fulfillments.Count);
        }
    }
}
