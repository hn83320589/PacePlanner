using PacePlanner.Core.Course;

namespace PacePlanner.Core.Pacing;

/// <summary>
/// Splits the race into 1 km segments and assigns each a pace so that running time plus
/// aid-station stops equals the target finish time exactly (to the second).
/// </summary>
public static class PaceCalculator
{
    /// <summary>Uphill: +3.3 % time per +1 % grade.</summary>
    public const double UphillCostPerPercent = 0.033;

    /// <summary>Downhill: −1.8 % time per −1 % grade.</summary>
    public const double DownhillGainPerPercent = 0.018;

    /// <summary>Downhill benefit stops growing beyond this grade (braking on steep descents).</summary>
    public const double MaxBeneficialDownhillPercent = 8;

    /// <summary>Uphill grades above this are clamped (GPS spikes, stairs).</summary>
    public const double MaxUphillPercent = 30;

    private const double Epsilon = 1e-9;

    public static PacePlan Calculate(CourseProfile? course, PlanSettings settings)
    {
        Validate(settings);

        var totalKm = settings.DistanceKm;
        var scaled = course is null || course.Stats.TotalDistanceKm <= 0 ? null : course.ScaleTo(totalKm);
        var bounds = SegmentBounds(totalKm);
        var aidKms = settings.Aid.PositionsFor(totalKm);
        var stopSeconds = Math.Max(0, settings.Aid.StopSeconds);
        var runningSeconds = settings.TargetSeconds - aidKms.Count * stopSeconds;
        if (runningSeconds <= 0)
        {
            throw new PlanningException(
                $"補給站停留總時間（{aidKms.Count} 站 × {stopSeconds} 秒）已超過目標完賽時間，請調整補給設定。");
        }

        var grades = bounds.Select(b => GradePercent(scaled, b.Start, b.End)).ToArray();
        var paces = settings.Strategy switch
        {
            PaceStrategy.Even => EvenPaces(bounds, runningSeconds, totalKm),
            PaceStrategy.NegativeSplit => NegativeSplitPaces(bounds, runningSeconds, totalKm, settings.NegativeSplitSeconds),
            PaceStrategy.GradeAdjusted => GradeAdjustedPaces(bounds, grades, runningSeconds),
            _ => throw new PlanningException($"未知的配速策略：{settings.Strategy}"),
        };

        var rawTimes = bounds.Select((b, i) => paces[i] * (b.End - b.Start)).ToArray();
        var runTimes = RoundPreservingTotal(rawTimes, runningSeconds);

        var segments = new List<PaceSegment>(bounds.Count);
        var cumulative = 0;
        for (var i = 0; i < bounds.Count; i++)
        {
            var (start, end) = bounds[i];
            var stops = aidKms.Count(km => km > start + Epsilon && km <= end + Epsilon) * stopSeconds;
            cumulative += runTimes[i] + stops;
            segments.Add(new PaceSegment(i + 1, start, end, grades[i], paces[i], runTimes[i], stops, cumulative));
        }

        return new PacePlan(settings, segments, aidKms, runningSeconds);
    }

    /// <summary>Time multiplier for a grade (in percent) relative to flat ground.</summary>
    public static double GradeFactor(double gradePercent)
    {
        if (gradePercent >= 0)
        {
            return 1 + UphillCostPerPercent * Math.Min(gradePercent, MaxUphillPercent);
        }

        return 1 - DownhillGainPerPercent * Math.Min(-gradePercent, MaxBeneficialDownhillPercent);
    }

    /// <summary>
    /// Rounds values to integers whose sum is exactly <paramref name="total"/> (largest remainder method),
    /// so every displayed split adds up to the target time.
    /// </summary>
    public static int[] RoundPreservingTotal(IReadOnlyList<double> values, int total)
    {
        var result = values.Select(v => (int)Math.Floor(v)).ToArray();
        var remainder = total - result.Sum();
        var order = values
            .Select((v, i) => (Fraction: v - Math.Floor(v), Index: i))
            .OrderByDescending(x => x.Fraction)
            .ThenBy(x => x.Index)
            .ToList();
        for (var k = 0; k < remainder; k++)
        {
            result[order[k % order.Count].Index]++;
        }

        return result;
    }

    private static void Validate(PlanSettings s)
    {
        if (!(s.DistanceKm > 0) || !double.IsFinite(s.DistanceKm))
        {
            throw new PlanningException("比賽距離必須大於 0 公里。");
        }

        if (s.TargetSeconds <= 0)
        {
            throw new PlanningException("目標完賽時間必須大於 0。");
        }

        if (s.Aid.StopSeconds < 0)
        {
            throw new PlanningException("補給站停留秒數不可為負數。");
        }

        if (s.Aid.Mode == AidStationMode.EveryNKm && !(s.Aid.IntervalKm > 0))
        {
            throw new PlanningException("「每 N 公里」補給的間隔必須大於 0。");
        }
    }

    private static List<(double Start, double End)> SegmentBounds(double totalKm)
    {
        var count = (int)Math.Ceiling(totalKm - Epsilon);
        return Enumerable.Range(0, count)
            .Select(i => ((double)i, Math.Min(i + 1, totalKm)))
            .ToList();
    }

    private static double GradePercent(CourseProfile? course, double startKm, double endKm)
    {
        var a = course?.ElevationAt(startKm);
        var b = course?.ElevationAt(endKm);
        if (a is null || b is null)
        {
            return 0;
        }

        return (b.Value - a.Value) / ((endKm - startKm) * 1000) * 100;
    }

    private static double[] EvenPaces(List<(double Start, double End)> bounds, int runningSeconds, double totalKm) =>
        bounds.Select(_ => runningSeconds / totalKm).ToArray();

    private static double[] NegativeSplitPaces(
        List<(double Start, double End)> bounds, int runningSeconds, double totalKm, int n)
    {
        // p1·(D/2) + (p1 − N)·(D/2) = T  ⇒  p1 = (T + N·D/2) / D
        var half = totalKm / 2;
        var p1 = (runningSeconds + n * half) / totalKm;
        var p2 = p1 - n;
        if (p2 <= 0 || p1 <= 0)
        {
            throw new PlanningException($"負分段秒數 {n} 太大，後半程配速會小於等於 0，請調小。");
        }

        return bounds.Select(b =>
        {
            var firstPart = Math.Max(0, Math.Min(b.End, half) - b.Start);
            var secondPart = Math.Max(0, b.End - Math.Max(b.Start, half));
            return (p1 * firstPart + p2 * secondPart) / (b.End - b.Start);
        }).ToArray();
    }

    private static double[] GradeAdjustedPaces(
        List<(double Start, double End)> bounds, double[] grades, int runningSeconds)
    {
        // pace_i = base · f(grade_i), base chosen so Σ d_i · pace_i = T.
        var factors = grades.Select(GradeFactor).ToArray();
        var weighted = bounds.Select((b, i) => (b.End - b.Start) * factors[i]).Sum();
        var basePace = runningSeconds / weighted;
        return factors.Select(f => basePace * f).ToArray();
    }
}
