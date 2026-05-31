using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gbt.Service;

/// <summary>
/// Lightweight liveness heartbeat. Logs once a minute so the Event Log shows the
/// service is alive between user interactions. Replaced or extended in Phase 1
/// by a sensor broadcaster that fans out to subscribed JSON-RPC clients.
/// </summary>
public sealed class HeartbeatWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(ILogger<HeartbeatWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _logger.LogInformation("Heartbeat at {Now:O}", DateTimeOffset.UtcNow);
        }
        _logger.LogInformation("Heartbeat worker stopping");
    }
}
