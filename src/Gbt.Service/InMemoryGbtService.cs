using System.Runtime.CompilerServices;
using Gbt.Common;
using Microsoft.Extensions.Logging;

namespace Gbt.Service;

/// <summary>
/// Phase-0 stub implementation of <see cref="IGbtService"/>. Holds an in-memory
/// <see cref="PerformanceProfile"/> and <see cref="BatteryChargeLimit"/>, and
/// emits a synthetic <see cref="SensorSnapshot"/> every second.
/// <para>
/// Replaced in Phase 1 with a real implementation backed by
/// <c>Gbt.Hardware.WinRing0EcController</c>, <c>WinRing0MsrController</c>,
/// <c>WmiClient</c> and <c>LhmSensorService</c>. The IPC contract surface
/// (the methods on this class) is stable as of Phase 0 and is what the WPF UI
/// in the follow-up task will bind to.
/// </para>
/// </summary>
public sealed class InMemoryGbtService : IGbtService
{
    private static readonly FanCurve DefaultFanCurve = new(
        cpu: new[]
        {
            new FanCurvePoint(40, 25),
            new FanCurvePoint(60, 40),
            new FanCurvePoint(75, 65),
            new FanCurvePoint(90, 95),
        },
        gpu: new[]
        {
            new FanCurvePoint(45, 30),
            new FanCurvePoint(65, 45),
            new FanCurvePoint(80, 70),
            new FanCurvePoint(90, 100),
        });

    private readonly ILogger<InMemoryGbtService> _logger;
    private readonly object _gate = new();

    private PerformanceProfile _profile = new(PerformanceMode.Normal, 45, 60, DefaultFanCurve);
    private BatteryChargeLimit _battery = new(100);

    public InMemoryGbtService(ILogger<InMemoryGbtService> logger)
    {
        _logger = logger;
    }

    public Task<SensorSnapshot> GetSnapshotAsync() => Task.FromResult(BuildSnapshot());

    public Task<PerformanceProfile> GetProfileAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_profile);
        }
    }

    public Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        lock (_gate)
        {
            _profile = mode switch
            {
                PerformanceMode.Quiet => new PerformanceProfile(mode, 35, 45, DefaultFanCurve),
                PerformanceMode.Normal => new PerformanceProfile(mode, 45, 60, DefaultFanCurve),
                PerformanceMode.Gaming => new PerformanceProfile(mode, 60, 90, DefaultFanCurve),
                PerformanceMode.Boost => new PerformanceProfile(mode, 90, 110, DefaultFanCurve),
                PerformanceMode.Custom => _profile with { Mode = PerformanceMode.Custom },
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown performance mode."),
            };
            _logger.LogInformation("Performance mode set to {Mode} (PL1={Pl1}W PL2={Pl2}W) [in-memory]",
                _profile.Mode, _profile.Pl1Watts, _profile.Pl2Watts);
        }
        return Task.CompletedTask;
    }

    public Task SetCustomProfileAsync(PerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Mode != PerformanceMode.Custom)
        {
            throw new ArgumentException("SetCustomProfileAsync requires Mode == Custom.", nameof(profile));
        }
        lock (_gate)
        {
            _profile = profile;
            _logger.LogInformation("Custom profile applied (PL1={Pl1}W PL2={Pl2}W) [in-memory]",
                profile.Pl1Watts, profile.Pl2Watts);
        }
        return Task.CompletedTask;
    }

    public Task<BatteryChargeLimit> GetBatteryChargeLimitAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_battery);
        }
    }

    public Task SetBatteryChargeLimitAsync(BatteryChargeLimit limit)
    {
        ArgumentNullException.ThrowIfNull(limit);
        lock (_gate)
        {
            _battery = limit;
            _logger.LogInformation("Battery charge limit set to {Percent}% [in-memory]", limit.Percent);
        }
        return Task.CompletedTask;
    }

    public Task<DiagnosticsReport> RunDiagnosticsAsync()
    {
        var report = new DiagnosticsReport(
            wmiClassesFound: Array.Empty<string>(),
            ecRegistersDumped: Array.Empty<int>(),
            warnings: new[]
            {
                "[Phase 0] No hardware backend is wired yet.",
                "[Phase 0] InMemoryGbtService is serving stub values.",
                "Run Gbt.Tools.DumpEc with Administrator rights to inspect EC/MSR/WMI on real hardware.",
            },
            serviceVersion: typeof(InMemoryGbtService).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        return Task.FromResult(report);
    }

    public async IAsyncEnumerable<SensorSnapshot> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return BuildSnapshot();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private SensorSnapshot BuildSnapshot()
    {
        int pl1, pl2;
        lock (_gate)
        {
            pl1 = _profile.Pl1Watts;
            pl2 = _profile.Pl2Watts;
        }
        return new SensorSnapshot(
            at: DateTimeOffset.UtcNow,
            cpuPackageC: double.NaN,
            gpuC: double.NaN,
            cpuFanRpm: 0,
            gpuFanRpm: 0,
            batteryPercent: 0,
            batteryCharging: false,
            batteryWatts: 0,
            pl1Watts: pl1,
            pl2Watts: pl2,
            modelName: "AORUS 15G KC (stub)");
    }
}
