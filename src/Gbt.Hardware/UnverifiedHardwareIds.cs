namespace Gbt.Hardware;

public sealed record UnverifiedHardwareId(string Name, string Value, string Purpose);

public static class UnverifiedHardwareIds
{
    public const uint WmbcSetPerformanceMode = 0x00000001; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device
    public const uint WmbcSetBatteryChargeLimit = 0x00000002; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device
    public const uint WmbdGetBatteryChargeLimit = 0x00000003; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device

    public static IReadOnlyList<UnverifiedHardwareId> All { get; } = new[]
    {
        new UnverifiedHardwareId(nameof(RegisterWhitelist.CpuFanDutyRegister), $"0x{RegisterWhitelist.CpuFanDutyRegister:X2}", "CPU fan duty EC write"),
        new UnverifiedHardwareId(nameof(RegisterWhitelist.GpuFanDutyRegister), $"0x{RegisterWhitelist.GpuFanDutyRegister:X2}", "GPU fan duty EC write"),
        new UnverifiedHardwareId(nameof(RegisterWhitelist.FanControlModeRegister), $"0x{RegisterWhitelist.FanControlModeRegister:X2}", "Fan auto/manual EC mode"),
        new UnverifiedHardwareId(nameof(WmbcSetPerformanceMode), $"0x{WmbcSetPerformanceMode:X8}", "GBT WMI performance flag"),
        new UnverifiedHardwareId(nameof(WmbcSetBatteryChargeLimit), $"0x{WmbcSetBatteryChargeLimit:X8}", "GBT WMI battery limit setter"),
        new UnverifiedHardwareId(nameof(WmbdGetBatteryChargeLimit), $"0x{WmbdGetBatteryChargeLimit:X8}", "GBT WMI battery limit getter")
    };
}
