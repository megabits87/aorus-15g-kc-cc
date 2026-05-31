using FluentAssertions;
using Gbt.Common;
using Gbt.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Gbt.Hardware.Tests;

public class MsrPerformanceModeApplierTests
{
    private const uint MsrRaplPowerUnit = 0x606;
    private const uint MsrPkgPowerLimit = 0x610;

    private static FanCurve Curve() => new(
        new[] { new FanCurvePoint(40, 30) },
        new[] { new FanCurvePoint(40, 30) });

    [Fact]
    public void Apply_encodes_power_limits_with_unit_and_sets_enable_bits()
    {
        var msr = new Mock<IMsrController>();
        // powerUnit = 3 -> 8 counts per watt.
        msr.Setup(m => m.Read(MsrRaplPowerUnit, -1)).Returns(0x3);
        msr.Setup(m => m.Read(MsrPkgPowerLimit, -1)).Returns(0UL);

        ulong written = 0;
        msr.Setup(m => m.Write(MsrPkgPowerLimit, It.IsAny<ulong>(), -1))
           .Callback<uint, ulong, int>((_, v, _) => written = v);

        var applier = new MsrPerformanceModeApplier(msr.Object, NullLogger<MsrPerformanceModeApplier>.Instance);
        applier.Apply(new PerformanceProfile(PerformanceMode.Normal, 45, 60, Curve()));

        // PL1: 45 * 8 = 360 (0x168) + enable bit 15. PL2: 60 * 8 = 480 (0x1E0) + enable bit 15 of high dword.
        ulong expectedLow = 0x168 | (1UL << 15);
        ulong expectedHigh = 0x1E0 | (1UL << 15);
        ulong expected = (expectedHigh << 32) | expectedLow;

        written.Should().Be(expected);
    }

    [Fact]
    public void Apply_preserves_existing_time_window_and_clamp_bits()
    {
        var msr = new Mock<IMsrController>();
        msr.Setup(m => m.Read(MsrRaplPowerUnit, -1)).Returns(0x3);
        // Pre-existing clamp (bit 16) + time window bits that must survive.
        ulong existing = (1UL << 16) | (0x2UL << 17);
        msr.Setup(m => m.Read(MsrPkgPowerLimit, -1)).Returns(existing);

        ulong written = 0;
        msr.Setup(m => m.Write(MsrPkgPowerLimit, It.IsAny<ulong>(), -1))
           .Callback<uint, ulong, int>((_, v, _) => written = v);

        var applier = new MsrPerformanceModeApplier(msr.Object, NullLogger<MsrPerformanceModeApplier>.Instance);
        applier.Apply(new PerformanceProfile(PerformanceMode.Normal, 45, 60, Curve()));

        (written & (1UL << 16)).Should().NotBe(0, "clamp bit must be preserved");
        (written & (0x2UL << 17)).Should().NotBe(0, "PL1 time window must be preserved");
    }

    [Fact]
    public void Apply_throws_when_register_is_locked()
    {
        var msr = new Mock<IMsrController>();
        msr.Setup(m => m.Read(MsrRaplPowerUnit, -1)).Returns(0x3);
        msr.Setup(m => m.Read(MsrPkgPowerLimit, -1)).Returns(1UL << 63); // lock bit

        var applier = new MsrPerformanceModeApplier(msr.Object, NullLogger<MsrPerformanceModeApplier>.Instance);
        var act = () => applier.Apply(new PerformanceProfile(PerformanceMode.Normal, 45, 60, Curve()));

        act.Should().Throw<MsrLockedException>();
        msr.Verify(m => m.Write(It.IsAny<uint>(), It.IsAny<ulong>(), It.IsAny<int>()), Times.Never);
    }
}
