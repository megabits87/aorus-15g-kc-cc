using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Sane envelope for a laptop thermal sensor. Values outside this are almost
    /// certainly a programming error rather than a real reading.</summary>
    public const int MinTempCelsius = -20;
    public const int MaxTempCelsius = 130;

    public int TempCelsius { get; init; }
    public int DutyPercent { get; init; }

    public FanCurvePoint(int tempCelsius, int dutyPercent)
    {
        if (tempCelsius < MinTempCelsius || tempCelsius > MaxTempCelsius)
        {
            throw new ArgumentOutOfRangeException(nameof(tempCelsius),
                $"Fan curve temperature must be between {MinTempCelsius} and {MaxTempCelsius} °C.");
        }

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
        Cpu = Validate(cpu ?? throw new ArgumentNullException(nameof(cpu)), nameof(cpu));
        Gpu = Validate(gpu ?? throw new ArgumentNullException(nameof(gpu)), nameof(gpu));
    }

    /// <summary>
    /// A fan curve must have at least one point and be sorted by ascending temperature so the
    /// interpolation engine can walk it left-to-right without re-sorting on the hot path. Two
    /// points at the same temperature are rejected because the duty would be ambiguous.
    /// </summary>
    private static IReadOnlyList<FanCurvePoint> Validate(IReadOnlyList<FanCurvePoint> points, string paramName)
    {
        if (points.Count == 0)
        {
            throw new ArgumentException("A fan curve must contain at least one point.", paramName);
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].TempCelsius <= points[i - 1].TempCelsius)
            {
                throw new ArgumentException(
                    $"Fan curve points must be sorted by strictly ascending temperature; " +
                    $"point {i} ({points[i].TempCelsius} °C) is not greater than point {i - 1} ({points[i - 1].TempCelsius} °C).",
                    paramName);
            }
        }

        return points;
    }
}

public sealed record PerformanceProfile
{
    /// <summary>Lower/upper sanity bounds for package power limits, in watts. The 10870H tops out
    /// around 135 W PL2; we allow a little headroom but reject obviously bogus values.</summary>
    public const int MinWatts = 1;
    public const int MaxWatts = 200;

    public PerformanceMode Mode { get; init; }
    public int Pl1Watts { get; init; }
    public int Pl2Watts { get; init; }
    public FanCurve FanCurve { get; init; }

    public PerformanceProfile(PerformanceMode mode, int pl1Watts, int pl2Watts, FanCurve fanCurve)
    {
        if (pl1Watts < MinWatts || pl1Watts > MaxWatts)
        {
            throw new ArgumentOutOfRangeException(nameof(pl1Watts),
                $"PL1 must be between {MinWatts} and {MaxWatts} W.");
        }

        if (pl2Watts < MinWatts || pl2Watts > MaxWatts)
        {
            throw new ArgumentOutOfRangeException(nameof(pl2Watts),
                $"PL2 must be between {MinWatts} and {MaxWatts} W.");
        }

        if (pl2Watts < pl1Watts)
        {
            throw new ArgumentException(
                $"PL2 ({pl2Watts} W) must be greater than or equal to PL1 ({pl1Watts} W).", nameof(pl2Watts));
        }

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
