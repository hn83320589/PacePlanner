using PacePlanner.Core.Course;
using PacePlanner.Core.Gpx;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Tests.Pacing;

public class PaceCalculatorTests
{
    private const int ThreeThirty = 3 * 3600 + 30 * 60;

    private static PlanSettings Settings(
        double km = RaceDistances.MarathonKm,
        int target = ThreeThirty,
        PaceStrategy strategy = PaceStrategy.Even,
        int negativeSplit = 0,
        AidStationSettings? aid = null) =>
        new(km, target, strategy, negativeSplit, aid ?? AidStationSettings.None);

    /// <summary>Course along a meridian whose elevation at each kilometre mark is given.</summary>
    private static CourseProfile CourseWithElevations(params double[] elevationAtKm)
    {
        var points = elevationAtKm
            .Select((e, km) => new TrackPoint(km / GeoMath.HaversineMeters(0, 0, 1, 0) * 1000, 0, e))
            .ToList();
        return CourseProfile.FromTrack(new Track(null, [points]));
    }

    private static void AssertCumulativeConsistent(PacePlan plan)
    {
        var running = 0;
        foreach (var s in plan.Segments)
        {
            running += s.DurationSeconds;
            Assert.Equal(running, s.CumulativeSeconds);
        }
    }

    [Fact]
    public void Segments_AreOneKilometreWithPartialLastSegment()
    {
        var plan = PaceCalculator.Calculate(null, Settings());

        Assert.Equal(43, plan.Segments.Count);
        Assert.All(plan.Segments.SkipLast(1), s => Assert.Equal(1, s.DistanceKm, 9));
        Assert.Equal(42, plan.Segments[^1].StartKm, 9);
        Assert.Equal(0.195, plan.Segments[^1].DistanceKm, 9);
    }

    [Fact]
    public void Segments_WholeKilometreDistance_HasNoEmptyTail()
    {
        var plan = PaceCalculator.Calculate(null, Settings(km: 10, target: 3000));

        Assert.Equal(10, plan.Segments.Count);
    }

    [Fact]
    public void Even_AllPacesEqual_LastSegmentProportional_TotalExact()
    {
        var plan = PaceCalculator.Calculate(null, Settings());

        var pace = plan.Segments[0].PaceSecondsPerKm;
        Assert.All(plan.Segments, s => Assert.Equal(pace, s.PaceSecondsPerKm, 9));
        Assert.Equal(ThreeThirty / RaceDistances.MarathonKm, pace, 9);
        Assert.Equal(pace * 0.195, plan.Segments[^1].DurationSeconds, 1.0);
        Assert.Equal(ThreeThirty, plan.Segments[^1].CumulativeSeconds);
        Assert.Equal(ThreeThirty, plan.Segments.Sum(s => s.DurationSeconds));
        AssertCumulativeConsistent(plan);
    }

    [Fact]
    public void NegativeSplit_SecondHalfFasterByN_TotalExact()
    {
        var plan = PaceCalculator.Calculate(null, Settings(strategy: PaceStrategy.NegativeSplit, negativeSplit: 10));

        var half = RaceDistances.MarathonKm / 2;
        var first = plan.Segments.Where(s => s.EndKm <= half).ToList();
        var second = plan.Segments.Where(s => s.StartKm >= half).ToList();
        Assert.Equal(10, first[0].PaceSecondsPerKm - second[0].PaceSecondsPerKm, 9);
        Assert.True(second.Average(s => s.PaceSecondsPerKm) < first.Average(s => s.PaceSecondsPerKm));
        Assert.Equal(ThreeThirty, plan.Segments[^1].CumulativeSeconds);
        AssertCumulativeConsistent(plan);
    }

    [Fact]
    public void NegativeSplit_SegmentStraddlingHalfway_BlendsPaces()
    {
        var plan = PaceCalculator.Calculate(null, Settings(strategy: PaceStrategy.NegativeSplit, negativeSplit: 10));

        var straddle = plan.Segments[21]; // 21–22 km, halfway is 21.0975
        Assert.InRange(straddle.PaceSecondsPerKm, plan.Segments[^1].PaceSecondsPerKm, plan.Segments[0].PaceSecondsPerKm);
    }

    [Fact]
    public void NegativeSplit_ImpossiblyLargeN_Throws()
    {
        var ex = Assert.Throws<PlanningException>(() =>
            PaceCalculator.Calculate(null, Settings(km: 5, target: 1500, strategy: PaceStrategy.NegativeSplit, negativeSplit: 1000)));

        Assert.Contains("負分段", ex.Message);
    }

    [Fact]
    public void GradeAdjusted_UphillSlowerFlatMiddleDownhillFaster_TotalExact()
    {
        // km0-1 flat, km1-2 +3% (30 m), km2-3 flat, km3-4 -3%.
        var course = CourseWithElevations(100, 100, 130, 130, 100);

        var plan = PaceCalculator.Calculate(course, Settings(km: 4, target: 1200, strategy: PaceStrategy.GradeAdjusted));

        var flat = plan.Segments[0].PaceSecondsPerKm;
        Assert.Equal(3, plan.Segments[1].GradePercent, 6);
        Assert.Equal(-3, plan.Segments[3].GradePercent, 6);
        Assert.True(plan.Segments[1].PaceSecondsPerKm > flat);
        Assert.True(plan.Segments[3].PaceSecondsPerKm < flat);
        Assert.Equal(flat, plan.Segments[2].PaceSecondsPerKm, 9);
        Assert.Equal(1200, plan.Segments[^1].CumulativeSeconds);
        AssertCumulativeConsistent(plan);
    }

    [Fact]
    public void GradeAdjusted_WithoutElevation_EqualsEven()
    {
        var plan = PaceCalculator.Calculate(null, Settings(km: 10, target: 3000, strategy: PaceStrategy.GradeAdjusted));

        Assert.All(plan.Segments, s => Assert.Equal(300, s.PaceSecondsPerKm, 9));
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.033)]
    [InlineData(-1, 0.982)]
    [InlineData(-8, 1 - 0.018 * 8)]
    [InlineData(-15, 1 - 0.018 * 8)]
    [InlineData(40, 1 + 0.033 * 30)]
    public void GradeFactor_FollowsDocumentedCoefficients(double grade, double expected)
    {
        Assert.Equal(expected, PaceCalculator.GradeFactor(grade), 9);
    }

    [Fact]
    public void AidStations_EveryN_StopsCountedAndFinishStillOnTarget()
    {
        var aid = new AidStationSettings(AidStationMode.EveryNKm, 5, [], 30);

        var plan = PaceCalculator.Calculate(null, Settings(aid: aid));

        Assert.Equal(new[] { 5.0, 10, 15, 20, 25, 30, 35, 40 }, plan.AidStationKms);
        Assert.Equal(8 * 30, plan.Segments.Sum(s => s.AidStopSeconds));
        Assert.Equal(30, plan.Segments[4].AidStopSeconds); // 4–5 km segment holds the 5 km station
        Assert.Equal(ThreeThirty - 240, plan.RunningSeconds);
        Assert.Equal(ThreeThirty, plan.Segments[^1].CumulativeSeconds);
        Assert.Equal((ThreeThirty - 240) / RaceDistances.MarathonKm, plan.Segments[0].PaceSecondsPerKm, 9);
        AssertCumulativeConsistent(plan);
    }

    [Fact]
    public void AidStations_CustomList_FiltersOutOfRangeAndDuplicates()
    {
        var aid = new AidStationSettings(AidStationMode.CustomList, 0, [7.5, 2, 2, 0, 10, 12], 20);

        var plan = PaceCalculator.Calculate(null, Settings(km: 10, target: 3000, aid: aid));

        Assert.Equal(new[] { 2.0, 7.5 }, plan.AidStationKms);
        Assert.Equal(20, plan.Segments[1].AidStopSeconds);
        Assert.Equal(20, plan.Segments[7].AidStopSeconds);
        Assert.Equal(3000, plan.Segments[^1].CumulativeSeconds);
    }

    [Fact]
    public void AidStations_StopsExceedTarget_Throws()
    {
        var aid = new AidStationSettings(AidStationMode.EveryNKm, 1, [], 600);

        var ex = Assert.Throws<PlanningException>(() => PaceCalculator.Calculate(null, Settings(km: 5, target: 1200, aid: aid)));

        Assert.Contains("補給", ex.Message);
    }

    [Theory]
    [InlineData(0, 3000)]
    [InlineData(10, 0)]
    [InlineData(-1, 3000)]
    public void InvalidSettings_Throw(double km, int target)
    {
        Assert.Throws<PlanningException>(() => PaceCalculator.Calculate(null, Settings(km: km, target: target)));
    }

    [Fact]
    public void Course_IsScaledToPlanDistance()
    {
        var course = CourseWithElevations(0, 10, 20); // ~2 km of GPS, planned as 4 km

        var plan = PaceCalculator.Calculate(course, Settings(km: 4, target: 1200, strategy: PaceStrategy.GradeAdjusted));

        Assert.Equal(4, plan.Segments.Count);
        Assert.Equal(0.5, plan.Segments[0].GradePercent, 6); // 5 m over 1 km after scaling
    }

    [Fact]
    public void RoundPreservingTotal_RoundsCumulativeSoAdjustmentsAreSpread()
    {
        var rounded = PaceCalculator.RoundPreservingTotal([1.4, 1.4, 1.2], 4);

        Assert.Equal(new[] { 1, 2, 1 }, rounded); // cumulative 1.4→1, 2.8→3, 4.0→4
    }

    [Fact]
    public void Even_CumulativeColumnIsIdealTimeRoundedToNearestSecond()
    {
        var plan = PaceCalculator.Calculate(null, Settings());

        var pace = ThreeThirty / RaceDistances.MarathonKm;
        Assert.All(plan.Segments, s => Assert.Equal(Math.Round(pace * s.EndKm, MidpointRounding.AwayFromZero), s.CumulativeSeconds));
        Assert.All(plan.Segments.SkipLast(1), s => Assert.InRange(s.RunSeconds, 298, 299));
    }
}
