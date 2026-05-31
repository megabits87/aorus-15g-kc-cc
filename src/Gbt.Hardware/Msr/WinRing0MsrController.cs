using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Reads and writes model-specific registers through WinRing0. When <c>cpu</c> is negative the
/// access runs on whatever logical processor the call lands on; otherwise it is pinned to that
/// processor via the driver's affinity-aware entry points (package-scoped MSRs such as 0x610 are
/// identical on every core, so the unpinned path is fine for power limits).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinRing0MsrController : IMsrController
{
    private readonly WinRing0Driver _driver;
    private readonly ILogger<WinRing0MsrController> _logger;

    public WinRing0MsrController(WinRing0Driver driver, ILogger<WinRing0MsrController> logger)
    {
        _driver = driver;
        _logger = logger;
    }

    public ulong Read(uint msr, int cpu)
    {
        _driver.EnsureInitialized();
        lock (_driver.Gate)
        {
            if (cpu < 0)
            {
                return _driver.ReadMsr(msr);
            }

            uint eax = 0, edx = 0;
            var mask = checked((UIntPtr)(1UL << cpu));
            if (WinRing0Native.RdmsrTx(msr, ref eax, ref edx, mask) == 0)
            {
                throw new InvalidOperationException($"RDMSR 0x{msr:X} on CPU {cpu} failed.");
            }
            return ((ulong)edx << 32) | eax;
        }
    }

    public void Write(uint msr, ulong value, int cpu)
    {
        _driver.EnsureInitialized();
        lock (_driver.Gate)
        {
            if (cpu < 0)
            {
                _driver.WriteMsr(msr, value);
            }
            else
            {
                var eax = (uint)(value & 0xFFFFFFFF);
                var edx = (uint)(value >> 32);
                var mask = checked((UIntPtr)(1UL << cpu));
                if (WinRing0Native.WrmsrTx(msr, eax, edx, mask) == 0)
                {
                    throw new InvalidOperationException($"WRMSR 0x{msr:X} on CPU {cpu} failed.");
                }
            }
        }

        _logger.LogDebug("MSR 0x{Msr:X} <= 0x{Val:X16} (cpu {Cpu})", msr, value, cpu);
    }
}
