using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Gbt.Common;
using Gbt.Hardware;
using Microsoft.Extensions.Logging;

namespace Gbt.Service;

/// <summary>
/// Hardware-backed <see cref="IGbtService"/>. Composes the sensor reader, MSR power-limit applier, fan
/// curve engine and battery service into the IPC surface the Control Center binds to. Mutating
/// operations are applied to hardware (best-effort) and persisted so they survive a restart.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareGbtService : IGbtService
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

    private readonly ISensorService _sensors;
    private readonly IPerformanceModeApplier _applier;
    private readonly IFanCurveEngine _fans;
    private readonly IBatteryService _battery;
    private readonly IWmiClient _wmi;
    private readonly IEcController _ec;
    private readonly PerformanceProfilePersister _persister;
    private readonly ILogger<HardwareGbtService> _logger;
    private readonly object _gate = new();

    private PerformanceProfile _profile = BuildPreset(PerformanceMode.Normal);

    public HardwareGbtService(
        ISensorService sensors,
        IPerformanceModeApplier applier,
        IFanCurveEngine fans,
        IBatteryService battery,
        IWmiClient wmi,
        IEcController ec,
        PerformanceProfilePersister persister,
        ILogger<HardwareGbtService> logger)
    {
        _sensors = sensors;
        _applier = applier;
        _fans = fans;
        _battery = battery;
        _wmi = wmi;
        _ec = ec;
        _persister = persister;
        _logger = logger;
    }

    /// <summary>
    /// Loads persisted state and pushes it to hardware at startup. Every hardware touch is best-effort:
    /// if WinRing0 is missing the service still serves sensor data and remembers settings.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct)
    {
        var state = _persister.Load();
        lock (_gate)
        {
            _profile = state.Profile ?? BuildPreset(PerformanceMode.Normal);
        }

        SafeApply(_profile);

        try
        {
            _battery.SetChargeLimit(state.BatteryPercent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restore battery charge limit at startup");
        }

        return Task.CompletedTask;
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
        PerformanceProfile profile;
        lock (_gate)
        {
            profile = mode == PerformanceMode.Custom
                ? _profile with { Mode = PerformanceMode.Custom }
                : BuildPreset(mode);
            _profile = profile;
        }

        SafeApply(profile);
        Persist();
        _logger.LogInformation("Performance mode set to {Mode} (PL1={Pl1}W PL2={Pl2}W)",
            profile.Mode, profile.Pl1Watts, profile.Pl2Watts);
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
        }

        SafeApply(profile);
        Persist();
        return Task.CompletedTask;
    }

    public Task<BatteryChargeLimit> GetBatteryChargeLimitAsync()
    {
        var percent = _battery.GetChargeLimit();
        return Task.FromResult(new BatteryChargeLimit(percent));
    }

    public Task SetBatteryChargeLimitAsync(BatteryChargeLimit limit)
    {
        ArgumentNullException.ThrowIfNull(limit);
        _battery.SetChargeLimit(limit.Percent);
        Persist();
        return Task.CompletedTask;
    }

    public Task<DiagnosticsReport> RunDiagnosticsAsync()
    {
        var warnings = new List<string>();
        IReadOnlyList<string> classes;
        try
        {
            classes = _wmi.EnumerateClasses();
        }
        catch (Exception ex)
        {
            classes = Array.Empty<string>();
            warnings.Add($"WMI enumeration failed: {ex.Message}");
        }

        var registers = new List<int>();
        foreach (var reg in new byte[] { RegisterWhitelist.CpuFanDutyRegister, RegisterWhitelist.GpuFanDutyRegister, RegisterWhitelist.FanControlModeRegister })
        {
            try
            {
                _ = _ec.Read(reg);
                registers.Add(reg);
            }
            catch (Exception ex)
            {
                warnings.Add($"EC read 0x{reg:X2} failed: {ex.Message}");
            }
        }

        if (warnings.Count == 0)
        {
            warnings.Add("All probed subsystems responded.");
        }

        var report = new DiagnosticsReport(
            wmiClassesFound: classes,
            ecRegistersDumped: registers,
            warnings: warnings,
            serviceVersion: typeof(HardwareGbtService).Assembly.GetName().Version?.ToString() ?? "0.0.0");
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
        PerformanceProfile profile;
        lock (_gate)
        {
            profile = _profile;
        }

        SensorSnapshot snapshot;
        try
        {
            snapshot = _sensors.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sensor read failed; returning a degraded snapshot");
            snapshot = new SensorSnapshot(DateTimeOffset.UtcNow, double.NaN, double.NaN, 0, 0, 0, false, 0, 0, 0, "AORUS 15G KC");
        }

        // The sensor layer leaves PL1/PL2 at zero; overlay the active profile's limits.
        return snapshot with { Pl1Watts = profile.Pl1Watts, Pl2Watts = profile.Pl2Watts };
    }

    private void SafeApply(PerformanceProfile profile)
    {
        try
        {
            _applier.Apply(profile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply power limits (PL1={Pl1}W PL2={Pl2}W)", profile.Pl1Watts, profile.Pl2Watts);
        }

        try
        {
            _fans.SetCurve(profile.FanCurve);
            _fans.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply fan curve");
        }
    }

    private void Persist()
    {
        PerformanceProfile profile;
        lock (_gate)
        {
            profile = _profile;
        }

        int battery;
        try
        {
            battery = _battery.GetChargeLimit();
        }
        catch
        {
            battery = 100;
        }

        _persister.Save(new PersistedState(profile, battery));
    }

    private static PerformanceProfile BuildPreset(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Quiet => new PerformanceProfile(mode, 35, 45, DefaultFanCurve),
        PerformanceMode.Normal => new PerformanceProfile(mode, 45, 60, DefaultFanCurve),
        PerformanceMode.Gaming => new PerformanceProfile(mode, 60, 90, DefaultFanCurve),
        PerformanceMode.Boost => new PerformanceProfile(mode, 90, 110, DefaultFanCurve),
        PerformanceMode.Custom => new PerformanceProfile(PerformanceMode.Custom, 45, 60, DefaultFanCurve),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown performance mode."),
    };
}
