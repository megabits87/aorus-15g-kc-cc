namespace Gbt.Hardware;

public static class RegisterWhitelist
{
    public const byte CpuFanDutyRegister = 0xB0; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device
    public const byte GpuFanDutyRegister = 0xB1; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device
    public const byte FanControlModeRegister = 0xB2; // UNVERIFIED — confirm with Gbt.Tools.DumpEc on real device

    private static readonly HashSet<byte> Allowed = new()
    {
        CpuFanDutyRegister,
        GpuFanDutyRegister,
        FanControlModeRegister
    };

    public static IReadOnlySet<byte> AllowedWrites => Allowed;

    public static void AssertCanWrite(byte register)
    {
        if (!Allowed.Contains(register))
        {
            throw new EcAccessViolationException(register);
        }
    }
}
