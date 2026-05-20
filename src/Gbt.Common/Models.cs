using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gbt.Common;

public enum PerformanceMode
{
    Quiet,
    Normal,
    Gaming,
    Boost,
    Custom
}

public sealed record FanCurvePoint
{
    public int TempCelsius { get; init; }
    public int DutyPercent { get; init; }

    public FanCurvePoint(int tempCelsius, int dutyPercent)
    {
        if (dutyPercent < 0 || dutyPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), "Fan duty percent must be between 0 and 100.");
        }

        TempCelsius = tempCelsius;
        DutyPercent = dutyPercent;
    }
}

public sealed record FanCurve
{
    public IReadOnlyList<FanCurvePoint> Cpu { get; init; }
    public IReadOnlyList<FanCurvePoint> Gpu { get; init; }

    public FanCurve(IReadOnlyList<FanCurvePoint> cpu, IReadOnlyList<FanCurvePoint> gpu)
    {
        Cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        Gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
    }
}

public sealed record PerformanceProfile
{
    public PerformanceMode Mode { get; init; }
    public int Pl1Watts { get; init; }
    public int Pl2Watts { get; init; }
    public FanCurve FanCurve { get; init; }

    public PerformanceProfile(PerformanceMode mode, int pl1Watts, int pl2Watts, FanCurve fanCurve)
    {
        Mode = mode;
        Pl1Watts = pl1Watts;
        Pl2Watts = pl2Watts;
        FanCurve = fanCurve ?? throw new ArgumentNullException(nameof(fanCurve));
    }
}

public sealed record BatteryChargeLimit
{
    public int Percent { get; init; }

    public BatteryChargeLimit(int percent = 100)
    {
        if (percent < 50 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Battery charge limit must be between 50 and 100 percent.");
        }

        Percent = percent;
    }
}

public sealed record SensorSnapshot
{
    public DateTimeOffset At { get; init; }
    public double CpuPackageC { get; init; }
    public double GpuC { get; init; }
    public int CpuFanRpm { get; init; }
    public int GpuFanRpm { get; init; }
    public int BatteryPercent { get; init; }
    public bool BatteryCharging { get; init; }
    public double BatteryWatts { get; init; }
    public int Pl1Watts { get; init; }
    public int Pl2Watts { get; init; }
    public string ModelName { get; init; }

    public SensorSnapshot(DateTimeOffset at, double cpuPackageC, double gpuC, int cpuFanRpm, int gpuFanRpm, int batteryPercent, bool batteryCharging, double batteryWatts, int pl1Watts, int pl2Watts, string modelName)
    {
        At = at;
        CpuPackageC = cpuPackageC;
        GpuC = gpuC;
        CpuFanRpm = cpuFanRpm;
        GpuFanRpm = gpuFanRpm;
        BatteryPercent = batteryPercent;
        BatteryCharging = batteryCharging;
        BatteryWatts = batteryWatts;
        Pl1Watts = pl1Watts;
        Pl2Watts = pl2Watts;
        ModelName = modelName ?? string.Empty;
    }
}

public sealed record DiagnosticsReport
{
    public IReadOnlyList<string> WmiClassesFound { get; init; }
    public IReadOnlyList<int> EcRegistersDumped { get; init; }
    public IReadOnlyList<string> Warnings { get; init; }
    public string ServiceVersion { get; init; }

    public DiagnosticsReport(IReadOnlyList<string> wmiClassesFound, IReadOnlyList<int> ecRegistersDumped, IReadOnlyList<string> warnings, string serviceVersion)
    {
        WmiClassesFound = wmiClassesFound ?? throw new ArgumentNullException(nameof(wmiClassesFound));
        EcRegistersDumped = ecRegistersDumped ?? throw new ArgumentNullException(nameof(ecRegistersDumped));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        ServiceVersion = serviceVersion ?? string.Empty;
    }
}

public interface IGbtService
{
    Task<SensorSnapshot> GetSnapshotAsync();
    Task<PerformanceProfile> GetProfileAsync();
    Task SetPerformanceModeAsync(PerformanceMode mode);
    Task SetCustomProfileAsync(PerformanceProfile profile);
    Task<BatteryChargeLimit> GetBatteryChargeLimitAsync();
    Task SetBatteryChargeLimitAsync(BatteryChargeLimit limit);
    Task<DiagnosticsReport> RunDiagnosticsAsync();
    IAsyncEnumerable<SensorSnapshot> SubscribeAsync(CancellationToken ct);
}
