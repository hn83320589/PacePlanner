namespace PacePlanner.Core.Course;

public static class ElevationAnalyzer
{
    /// <summary>Default hysteresis threshold; filters typical GPS/barometer jitter.</summary>
    public const double DefaultThresholdMeters = 3;

    /// <summary>
    /// Cumulative gain and loss using a hysteresis threshold: a change is only counted once the
    /// elevation has moved at least <paramref name="thresholdMeters"/> away from the last reference.
    /// A steady climb is still counted in full; jitter smaller than the threshold is ignored.
    /// </summary>
    public static (double Ascent, double Descent) GainAndLoss(
        IEnumerable<double> elevations, double thresholdMeters = DefaultThresholdMeters)
    {
        double ascent = 0, descent = 0;
        double? reference = null;
        foreach (var e in elevations)
        {
            if (reference is null)
            {
                reference = e;
                continue;
            }

            var delta = e - reference.Value;
            if (delta >= thresholdMeters)
            {
                ascent += delta;
                reference = e;
            }
            else if (-delta >= thresholdMeters)
            {
                descent -= delta;
                reference = e;
            }
        }

        return (ascent, descent);
    }
}
