using System.Runtime.Versioning;
using Gbt.Common;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Applies a profile's PL1/PL2 to the package via <c>MSR_PKG_POWER_LIMIT</c> (0x610), encoding watts
/// using the power unit from <c>MSR_RAPL_POWER_UNIT</c> (0x606). Time windows and clamp bits are left
/// untouched; only the two power-limit fields and their enable bits are written. If the register is
/// locked (bit 63) the BIOS owns the limits and we refuse rather than fault.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsrPerformanceModeApplier : IPerformanceModeApplier
{
    private const uint MsrRaplPowerUnit = 0x606;
    private const uint MsrPkgPowerLimit = 0x610;

    private const ulong PowerLimitMask = 0x7FFF;          // bits [14:0]
    private const int Pl1EnableBit = 15;
    private const int Pl2Shift = 32;                       // PL2 limit lives in the high dword
    private const int Pl2EnableBit = 47;
    private const int LockBit = 63;

    private readonly IMsrController _msr;
    private readonly ILogger<MsrPerformanceModeApplier> _logger;

    public MsrPerformanceModeApplier(IMsrController msr, ILogger<MsrPerformanceModeApplier> logger)
    {
        _msr = msr;
        _logger = logger;
    }

    public void Apply(PerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var unitRaw = _msr.Read(MsrRaplPowerUnit, -1);
        var powerUnit = (int)(unitRaw & 0xF);          // watts per count = 1 / 2^powerUnit
        var countsPerWatt = Math.Pow(2, powerUnit);

        var current = _msr.Read(MsrPkgPowerLimit, -1);
        if ((current & (1UL << LockBit)) != 0)
        {
            throw new MsrLockedException();
        }

        var pl1 = ToCounts(profile.Pl1Watts, countsPerWatt);
        var pl2 = ToCounts(profile.Pl2Watts, countsPerWatt);

        var updated = current;
        updated &= ~PowerLimitMask;                    // clear PL1 limit field
        updated |= pl1;                                 // set PL1 limit
        updated |= 1UL << Pl1EnableBit;                 // enable PL1
        updated &= ~(PowerLimitMask << Pl2Shift);       // clear PL2 limit field
        updated |= pl2 << Pl2Shift;                     // set PL2 limit
        updated |= 1UL << Pl2EnableBit;                 // enable PL2

        _msr.Write(MsrPkgPowerLimit, updated, -1);

        _logger.LogInformation(
            "Applied power limits PL1={Pl1}W PL2={Pl2}W (powerUnit=2^-{Pu}, 0x610: 0x{Old:X16} -> 0x{New:X16})",
            profile.Pl1Watts, profile.Pl2Watts, powerUnit, current, updated);
    }

    private static ulong ToCounts(int watts, double countsPerWatt)
    {
        var counts = Math.Round(watts * countsPerWatt, MidpointRounding.AwayFromZero);
        return (ulong)Math.Clamp(counts, 0, PowerLimitMask);
    }
}
