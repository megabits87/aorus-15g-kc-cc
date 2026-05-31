using System.Runtime.Versioning;
using Gbt.Common;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Drives the fans from a <see cref="FanCurve"/>: a 2 Hz loop reads the latest temperatures, evaluates
/// the curve with <see cref="FanCurveInterpolator"/>, and writes the resulting duty to the EC fan
/// registers. Starting the engine switches the EC to manual fan mode; stopping it hands control back
/// to the firmware (auto). All EC writes go through the whitelisted <see cref="IEcController"/>.
/// <para>
/// The duty scaling (0-100 % mapped to 0-255) and the manual/auto mode values are UNVERIFIED for the
/// AORUS 15G KC — confirm with <c>Gbt.Tools.DumpEc</c> before relying on them.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FanCurveEngine : IFanCurveEngine, IDisposable
{
    private const byte ManualMode = 0x01;   // UNVERIFIED
    private const byte AutoMode = 0x00;     // UNVERIFIED
    private const int EcDutyMax = 255;      // UNVERIFIED — some ECs use 0-100 or 0-229
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly IEcController _ec;
    private readonly ISensorService _sensors;
    private readonly ILogger<FanCurveEngine> _logger;
    private readonly object _gate = new();

    private FanCurve _curve;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public FanCurveEngine(IEcController ec, ISensorService sensors, ILogger<FanCurveEngine> logger)
    {
        _ec = ec;
        _sensors = sensors;
        _logger = logger;
        _curve = DefaultCurve();
    }

    public void SetCurve(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        lock (_gate)
        {
            _curve = curve;
        }
        _logger.LogInformation("Fan curve updated ({Cpu} CPU points, {Gpu} GPU points)", curve.Cpu.Count, curve.Gpu.Count);
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loop is { IsCompleted: false })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token), token);
        }
        _logger.LogInformation("Fan curve engine started");
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // cancellation is expected
        }
        cts.Dispose();

        TryRestoreAuto();
        _logger.LogInformation("Fan curve engine stopped; fans returned to firmware control");
    }

    private async Task RunAsync(CancellationToken token)
    {
        TrySetMode(ManualMode);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = _sensors.Read();
                FanCurve curve;
                lock (_gate)
                {
                    curve = _curve;
                }

                var cpuDuty = FanCurveInterpolator.DutyFor(curve.Cpu, snapshot.CpuPackageC);
                var gpuDuty = FanCurveInterpolator.DutyFor(curve.Gpu, snapshot.GpuC);

                _ec.Write(RegisterWhitelist.CpuFanDutyRegister, ToEcDuty(cpuDuty));
                _ec.Write(RegisterWhitelist.GpuFanDutyRegister, ToEcDuty(gpuDuty));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fan curve tick failed; will retry next interval");
            }

            try
            {
                await Task.Delay(Interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void TrySetMode(byte mode)
    {
        try
        {
            _ec.Write(RegisterWhitelist.FanControlModeRegister, mode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set EC fan mode to 0x{Mode:X2}", mode);
        }
    }

    private void TryRestoreAuto() => TrySetMode(AutoMode);

    private static byte ToEcDuty(int dutyPercent)
    {
        var scaled = (int)Math.Round(Math.Clamp(dutyPercent, 0, 100) / 100.0 * EcDutyMax);
        return (byte)Math.Clamp(scaled, 0, EcDutyMax);
    }

    private static FanCurve DefaultCurve() => new(
        cpu: new[]
        {
            new FanCurvePoint(40, 25),
            new FanCurvePoint(60, 40),
            new FanCurvePoint(75, 65),
            new FanCurvePoint(90, 100),
        },
        gpu: new[]
        {
            new FanCurvePoint(45, 30),
            new FanCurvePoint(65, 45),
            new FanCurvePoint(80, 70),
            new FanCurvePoint(90, 100),
        });

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
