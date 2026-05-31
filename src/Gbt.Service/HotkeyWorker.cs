using System.Management;
using System.Runtime.Versioning;
using Gbt.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gbt.Service;

/// <summary>
/// Listens for GIGABYTE ACPI-WMI hotkey events and advances the performance mode on each press
/// (Quiet → Normal → Gaming → Boost → Quiet). This replaces the firmware/OEM hotkey handler that the
/// stock AORUS Control Center provided.
/// <para>
/// The WMI event class name is UNVERIFIED for the AORUS 15G KC; if the class does not exist the worker
/// logs a warning and disables itself rather than faulting the service. Confirm the real event class
/// with <c>Gbt.Tools.DumpEc</c> and update <see cref="EventClass"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyWorker : BackgroundService
{
    private const string Namespace = @"\\.\root\WMI";
    private const string EventClass = "GB_WMIACPI_Event"; // UNVERIFIED

    private static readonly PerformanceMode[] Cycle =
    {
        PerformanceMode.Quiet,
        PerformanceMode.Normal,
        PerformanceMode.Gaming,
        PerformanceMode.Boost,
    };

    private readonly IGbtService _service;
    private readonly ILogger<HotkeyWorker> _logger;
    private ManagementEventWatcher? _watcher;

    public HotkeyWorker(IGbtService service, ILogger<HotkeyWorker> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var scope = new ManagementScope(Namespace);
            scope.Connect();

            _watcher = new ManagementEventWatcher(scope, new WqlEventQuery($"SELECT * FROM {EventClass}"));
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
            _logger.LogInformation("Listening for GBT hotkey events via {Class}", EventClass);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GBT hotkey WMI event class '{Class}' is unavailable; hotkeys disabled", EventClass);
        }

        return Task.CompletedTask;
    }

#pragma warning disable VSTHRD100 // WMI EventArrived handlers must return void; all exceptions are caught below.
    private async void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var current = (await _service.GetProfileAsync().ConfigureAwait(false)).Mode;
            var next = NextMode(current);
            await _service.SetPerformanceModeAsync(next).ConfigureAwait(false);
            _logger.LogInformation("Hotkey event -> performance mode {Mode}", next);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle hotkey event");
        }
    }
#pragma warning restore VSTHRD100

    private static PerformanceMode NextMode(PerformanceMode current)
    {
        var index = Array.IndexOf(Cycle, current);
        if (index < 0)
        {
            // Custom (or anything off-cycle) maps back to a known starting point.
            return PerformanceMode.Normal;
        }
        return Cycle[(index + 1) % Cycle.Length];
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            try
            {
                _watcher.EventArrived -= OnEventArrived;
                _watcher.Stop();
                _watcher.Dispose();
            }
            catch
            {
                // best-effort teardown
            }
            _watcher = null;
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
