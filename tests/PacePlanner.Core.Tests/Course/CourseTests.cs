using PacePlanner.Core.Course;
using PacePlanner.Core.Gpx;

namespace PacePlanner.Core.Tests.Course;

public class CourseTests
{
    // 0.001 degree of latitude is ~111.195 m with the mean Earth radius.
    private const double MetresPerMilliDegree = 111.19492664455873;

    private static Track Line(params double?[] elevations) =>
        new(null, [elevations.Select((e, i) => new TrackPoint(i * 0.001, 0, e)).ToList()]);

    [Fact]
    public void Haversine_OneDegreeOfLatitude_IsAbout111Km()
    {
        Assert.Equal(111_194.93, GeoMath.HaversineMeters(0, 0, 1, 0), 2);
    }

    [Fact]
    public void FromTrack_ComputesDistanceAndElevationStats()
    {
        var course = CourseProfile.FromTrack(Line(10, 20, 15, 30));

        Assert.Equal(3 * MetresPerMilliDegree / 1000, course.Stats.TotalDistanceKm, 6);
        Assert.Equal(25, course.Stats.AscentMeters, 6);
        Assert.Equal(5, course.Stats.DescentMeters, 6);
        Assert.Equal(30, course.Stats.MaxElevationMeters);
        Assert.Equal(10, course.Stats.MinElevationMeters);
        Assert.True(course.Stats.HasElevation);
    }

    [Fact]
    public void FromTrack_GapBetweenSegmentsIsNotCounted()
    {
        var track = new Track(null, [
            [new TrackPoint(0, 0, null), new TrackPoint(0.001, 0, null)],
            [new TrackPoint(0.5, 0, null), new TrackPoint(0.501, 0, null)],
        ]);

        var course = CourseProfile.FromTrack(track);

        Assert.Equal(2 * MetresPerMilliDegree / 1000, course.Stats.TotalDistanceKm, 6);
        Assert.Equal(4, course.Points.Count);
    }

    [Fact]
    public void Ascent_SmallNoiseBelowThreshold_IsIgnored()
    {
        var (ascent, descent) = ElevationAnalyzer.GainAndLoss([100, 102, 100, 102.5, 100, 101], thresholdMeters: 3);

        Assert.Equal(0, ascent);
        Assert.Equal(0, descent);
    }

    [Fact]
    public void Ascent_SteadyClimbInSmallSteps_IsFullyCounted()
    {
        var climb = Enumerable.Range(0, 31).Select(i => (double)i).ToList();

        var (ascent, descent) = ElevationAnalyzer.GainAndLoss(climb, thresholdMeters: 3);

        Assert.Equal(30, ascent);
        Assert.Equal(0, descent);
    }

    [Fact]
    public void FromTrack_MissingElevationPointsAreSkippedForStats()
    {
        var course = CourseProfile.FromTrack(Line(10, null, 20, null));

        Assert.Equal(10, course.Stats.AscentMeters);
        Assert.Equal(20, course.Stats.MaxElevationMeters);
    }

    [Fact]
    public void FromTrack_NoElevationAtAll_ReportsNoElevationAndFlatGrade()
    {
        var course = CourseProfile.FromTrack(Line(null, null, null));

        Assert.False(course.Stats.HasElevation);
        Assert.Null(course.Stats.MaxElevationMeters);
        Assert.Equal(0, course.Stats.AscentMeters);
        Assert.Null(course.ElevationAt(0.1));
    }

    [Fact]
    public void ScaleTo_StretchesDistancesButKeepsElevation()
    {
        var course = CourseProfile.FromTrack(Line(0, 10, 20));

        var scaled = course.ScaleTo(10);

        Assert.Equal(10, scaled.Stats.TotalDistanceKm, 9);
        Assert.Equal(5, scaled.Points[1].DistanceKm, 9);
        Assert.Equal(20, scaled.Stats.AscentMeters);
        Assert.Equal(course.Stats.TotalDistanceKm, scaled.Stats.OriginalDistanceKm, 9);
    }

    [Fact]
    public void ElevationAt_InterpolatesAndClampsAndSkipsMissing()
    {
        var course = CourseProfile.FromTrack(Line(0, null, 20)).ScaleTo(2);

        Assert.Equal(5, course.ElevationAt(0.5)!.Value, 9);
        Assert.Equal(10, course.ElevationAt(1)!.Value, 9);
        Assert.Equal(0, course.ElevationAt(-1));
        Assert.Equal(20, course.ElevationAt(99));
    }
}
