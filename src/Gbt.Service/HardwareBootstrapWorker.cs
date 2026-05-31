using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gbt.Service;

/// <summary>
/// Runs once at startup to push persisted settings onto the hardware via
/// <see cref="HardwareGbtService.InitializeAsync"/>. Kept as a hosted service (rather than doing the
/// work in the constructor) so failures are logged through the normal lifetime pipeline and never
/// prevent the JSON-RPC server from coming up.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareBootstrapWorker : BackgroundService
{
    private readonly HardwareGbtService _service;
    private readonly ILogger<HardwareBootstrapWorker> _logger;

    public HardwareBootstrapWorker(HardwareGbtService service, ILogger<HardwareBootstrapWorker> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _service.InitializeAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Hardware bootstrap complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware bootstrap failed; service will run in a degraded state");
        }
    }
}
