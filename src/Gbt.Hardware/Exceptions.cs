namespace Gbt.Hardware;

public sealed class EcAccessViolationException : InvalidOperationException
{
    public EcAccessViolationException(byte register)
        : base($"EC register 0x{register:X2} is not in the write whitelist. Refusing a potentially unsafe write.")
    {
        Register = register;
    }

    public byte Register { get; }
}

public sealed class WinRing0NotInstalledException : InvalidOperationException
{
    public WinRing0NotInstalledException(string message) : base(message)
    {
    }
}

public sealed class MsrLockedException : InvalidOperationException
{
    public MsrLockedException() : base("MSR_PKG_POWER_LIMIT is locked by bit 63; refusing to write PL1/PL2.")
    {
    }
}
