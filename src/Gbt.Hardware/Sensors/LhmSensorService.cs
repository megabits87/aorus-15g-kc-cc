using System.Runtime.Versioning;
using Gbt.Common;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Reads CPU/GPU temperatures, fan RPMs and battery state through LibreHardwareMonitor. The
/// <see cref="SensorSnapshot.Pl1Watts"/>/<see cref="SensorSnapshot.Pl2Watts"/> fields are left at zero
/// here; the service layer overlays them from the active <see cref="PerformanceProfile"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LhmSensorService : ISensorService, IDisposable
{
    private readonly ILogger<LhmSensorService> _logger;
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _gate = new();
    private string _modelName = "AORUS 15G KC";
    private bool _opened;
    private bool _disposed;

    public LhmSensorService(ILogger<LhmSensorService> logger)
    {
        _logger = logger;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsBatteryEnabled = true,
            IsControllerEnabled = true,
        };
    }

    private void EnsureOpen()
    {
        if (_opened)
        {
            return;
        }
        _computer.Open();
        _opened = true;

        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.Motherboard && !string.IsNullOrWhiteSpace(hw.Name))
            {
                _modelName = hw.Name;
                break;
            }
        }
        _logger.LogInformation("LibreHardwareMonitor opened (model '{Model}')", _modelName);
    }

    public SensorSnapshot Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureOpen();
            _computer.Accept(_visitor);

            double cpuTemp = double.NaN, gpuTemp = double.NaN;
            int batteryPercent = 0;
            bool charging = false;
            double batteryWatts = 0;
            var fanRpms = new List<int>();

            foreach (var hw in _computer.Hardware)
            {
                CollectFromHardware(hw, ref cpuTemp, ref gpuTemp, ref batteryPercent, ref charging, ref batteryWatts, fanRpms);
            }

            var cpuFan = fanRpms.Count > 0 ? fanRpms[0] : 0;
            var gpuFan = fanRpms.Count > 1 ? fanRpms[1] : 0;

            return new SensorSnapshot(
                at: DateTimeOffset.UtcNow,
                cpuPackageC: cpuTemp,
                gpuC: gpuTemp,
                cpuFanRpm: cpuFan,
                gpuFanRpm: gpuFan,
                batteryPercent: batteryPercent,
                batteryCharging: charging,
                batteryWatts: batteryWatts,
                pl1Watts: 0,
                pl2Watts: 0,
                modelName: _modelName);
        }
    }

    private static void CollectFromHardware(
        IHardware hw,
        ref double cpuTemp,
        ref double gpuTemp,
        ref int batteryPercent,
        ref bool charging,
        ref double batteryWatts,
        List<int> fanRpms)
    {
        foreach (var sensor in hw.Sensors)
        {
            var value = sensor.Value;
            if (value is null)
            {
                continue;
            }

            switch (sensor.SensorType)
            {
                case SensorType.Temperature when hw.HardwareType == HardwareType.Cpu:
                    if (IsPackageTemp(sensor.Name) || double.IsNaN(cpuTemp))
                    {
                        cpuTemp = value.Value;
                    }
                    break;

                case SensorType.Temperature when IsGpu(hw.HardwareType):
                    if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || double.IsNaN(gpuTemp))
                    {
                        gpuTemp = value.Value;
                    }
                    break;

                case SensorType.Fan:
                    fanRpms.Add((int)Math.Round(value.Value));
                    break;

                case SensorType.Level when hw.HardwareType == HardwareType.Battery
                                           && sensor.Name.Contains("Charge", StringComparison.OrdinalIgnoreCase):
                    batteryPercent = (int)Math.Round(Math.Clamp(value.Value, 0, 100));
                    break;

                case SensorType.Power when hw.HardwareType == HardwareType.Battery:
                    if (sensor.Name.Contains("Charge", StringComparison.OrdinalIgnoreCase)
                        && !sensor.Name.Contains("Discharge", StringComparison.OrdinalIgnoreCase))
                    {
                        charging = value.Value > 0.01;
                        batteryWatts = value.Value;
                    }
                    else if (sensor.Name.Contains("Discharge", StringComparison.OrdinalIgnoreCase))
                    {
                        batteryWatts = -value.Value;
                    }
                    break;
            }
        }

        foreach (var sub in hw.SubHardware)
        {
            CollectFromHardware(sub, ref cpuTemp, ref gpuTemp, ref batteryPercent, ref charging, ref batteryWatts, fanRpms);
        }
    }

    private static bool IsPackageTemp(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
        || name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (_opened)
            {
                try { _computer.Close(); } catch { /* best effort */ }
                _opened = false;
            }
            _disposed = true;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
