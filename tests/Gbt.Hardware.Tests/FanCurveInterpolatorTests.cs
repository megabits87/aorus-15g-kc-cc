using FluentAssertions;
using Gbt.Common;
using Gbt.Hardware;
using Xunit;

namespace Gbt.Hardware.Tests;

public class FanCurveInterpolatorTests
{
    private static readonly FanCurvePoint[] Curve =
    {
        new(40, 20),
        new(60, 40),
        new(80, 80),
        new(90, 100),
    };

    [Fact]
    public void Below_first_point_holds_first_duty()
    {
        FanCurveInterpolator.DutyFor(Curve, 30).Should().Be(20);
    }

    [Fact]
    public void Above_last_point_holds_last_duty()
    {
        FanCurveInterpolator.DutyFor(Curve, 120).Should().Be(100);
    }

    [Fact]
    public void Exact_point_returns_its_duty()
    {
        FanCurveInterpolator.DutyFor(Curve, 60).Should().Be(40);
    }

    [Fact]
    public void Midpoint_is_linearly_interpolated()
    {
        // Halfway between (40,20) and (60,40) -> 50 °C -> 30 %.
        FanCurveInterpolator.DutyFor(Curve, 50).Should().Be(30);
    }

    [Fact]
    public void Quarter_point_rounds_to_nearest_percent()
    {
        // Between (60,40) and (80,80): at 65 °C ratio 0.25 -> 40 + 0.25*40 = 50.
        FanCurveInterpolator.DutyFor(Curve, 65).Should().Be(50);
    }

    [Fact]
    public void Nan_fails_safe_to_maximum_duty()
    {
        FanCurveInterpolator.DutyFor(Curve, double.NaN).Should().Be(100);
    }

    [Fact]
    public void Single_point_curve_always_returns_that_duty()
    {
        var single = new FanCurvePoint[] { new(50, 55) };
        FanCurveInterpolator.DutyFor(single, 10).Should().Be(55);
        FanCurveInterpolator.DutyFor(single, 99).Should().Be(55);
    }

    [Fact]
    public void Empty_curve_throws()
    {
        var act = () => FanCurveInterpolator.DutyFor(System.Array.Empty<FanCurvePoint>(), 50);
        act.Should().Throw<System.ArgumentException>();
    }
}
