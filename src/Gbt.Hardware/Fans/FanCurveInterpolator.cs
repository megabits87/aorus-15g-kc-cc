using Gbt.Common;

namespace Gbt.Hardware;

/// <summary>
/// Pure, side-effect-free evaluation of a <see cref="FanCurve"/>. Kept separate from
/// <c>FanCurveEngine</c> (which owns the timer and EC writes) so the interpolation maths can be
/// unit-tested without any hardware. The curve points are assumed sorted ascending by temperature,
/// which <see cref="FanCurve"/> guarantees at construction time.
/// </summary>
public static class FanCurveInterpolator
{
    /// <summary>
    /// Returns the fan duty (0-100) for <paramref name="tempCelsius"/> by linearly interpolating
    /// between the two surrounding curve points. Below the first point the first duty is held; above
    /// the last point the last duty is held (no extrapolation — we never exceed the curve's intent).
    /// </summary>
    public static int DutyFor(IReadOnlyList<FanCurvePoint> points, double tempCelsius)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("Curve must contain at least one point.", nameof(points));
        }

        if (double.IsNaN(tempCelsius))
        {
            // A bad sensor read must never silently spin the fans down. Fail safe to the warmest point.
            return points[^1].DutyPercent;
        }

        if (tempCelsius <= points[0].TempCelsius)
        {
            return points[0].DutyPercent;
        }

        if (tempCelsius >= points[^1].TempCelsius)
        {
            return points[^1].DutyPercent;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var hi = points[i];
            if (tempCelsius > hi.TempCelsius)
            {
                continue;
            }

            var lo = points[i - 1];
            var span = hi.TempCelsius - lo.TempCelsius;
            if (span <= 0)
            {
                return hi.DutyPercent;
            }

            var ratio = (tempCelsius - lo.TempCelsius) / span;
            var duty = lo.DutyPercent + ratio * (hi.DutyPercent - lo.DutyPercent);
            return (int)Math.Round(Math.Clamp(duty, 0, 100), MidpointRounding.AwayFromZero);
        }

        return points[^1].DutyPercent;
    }
}
