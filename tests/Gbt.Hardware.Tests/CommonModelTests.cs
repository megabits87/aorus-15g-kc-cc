using FluentAssertions;
using Gbt.Common;
using Xunit;

namespace Gbt.Hardware.Tests;

public class CommonModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void FanCurvePoint_accepts_duty_in_range(int duty)
    {
        var act = () => new FanCurvePoint(60, duty);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void FanCurvePoint_rejects_out_of_range_duty(int duty)
    {
        var act = () => new FanCurvePoint(60, duty);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void BatteryChargeLimit_accepts_valid_percent(int percent)
    {
        new BatteryChargeLimit(percent).Percent.Should().Be(percent);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    public void BatteryChargeLimit_rejects_out_of_range_percent(int percent)
    {
        var act = () => new BatteryChargeLimit(percent);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BatteryChargeLimit_default_is_one_hundred()
    {
        new BatteryChargeLimit().Percent.Should().Be(100);
    }

    [Fact]
    public void FanCurve_requires_both_lists()
    {
        var pts = new[] { new FanCurvePoint(40, 30) };
        var act1 = () => new FanCurve(null!, pts);
        var act2 = () => new FanCurve(pts, null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PerformanceProfile_requires_fan_curve()
    {
        var act = () => new PerformanceProfile(PerformanceMode.Normal, 45, 60, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SensorSnapshot_treats_null_model_name_as_empty()
    {
        var snap = new SensorSnapshot(
            DateTimeOffset.UnixEpoch, 60, 55, 2000, 2100, 90, true, -25.5,
            45, 60, null!);
        snap.ModelName.Should().BeEmpty();
    }

    [Fact]
    public void DiagnosticsReport_rejects_null_collections()
    {
        var classes = new[] { "GBT_WMI" };
        var regs = new[] { 0x60, 0xB0 };
        var warnings = new[] { "stub" };

        var actClasses = () => new DiagnosticsReport(null!, regs, warnings, "v0");
        var actRegs = () => new DiagnosticsReport(classes, null!, warnings, "v0");
        var actWarn = () => new DiagnosticsReport(classes, regs, null!, "v0");

        actClasses.Should().Throw<ArgumentNullException>();
        actRegs.Should().Throw<ArgumentNullException>();
        actWarn.Should().Throw<ArgumentNullException>();
    }
}
