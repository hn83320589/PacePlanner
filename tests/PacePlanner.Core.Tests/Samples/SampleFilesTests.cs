using PacePlanner.Core.Course;
using PacePlanner.Core.Gpx;

namespace PacePlanner.Core.Tests.Samples;

/// <summary>Checks the shipped sample files against samples/expected-stats.md.</summary>
public class SampleFilesTests
{
    private const double OneMetreKm = 0.001;

    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    private static CourseProfile Load(string name) => CourseProfile.FromTrack(GpxParser.ParseFile(SamplePath(name)));

    [Fact]
    public void RiverPark10k_MatchesExpectedStats()
    {
        var stats = Load("river-park-10k.gpx").Stats;

        Assert.Equal(10.000, stats.TotalDistanceKm, OneMetreKm);
        Assert.Equal(60, stats.AscentMeters, 6);
        Assert.Equal(60, stats.DescentMeters, 6);
        Assert.Equal(55, stats.MaxElevationMeters);
        Assert.Equal(10, stats.MinElevationMeters);
    }

    [Fact]
    public void MultiSegment5k_ImportsAllSegmentsInOrderAndSkipsGap()
    {
        var track = GpxParser.ParseFile(SamplePath("multi-segment-5k.gpx"));
        var stats = CourseProfile.FromTrack(track).Stats;

        Assert.Equal(new[] { 21, 13, 17 }, track.Segments.Select(s => s.Count));
        var latitudes = track.Points.Select(p => p.Latitude).ToList();
        Assert.True(latitudes.Zip(latitudes.Skip(1)).All(pair => pair.Second >= pair.First), "points must keep document order");
        Assert.Equal(4.800, stats.TotalDistanceKm, OneMetreKm);
        Assert.Equal(42, stats.AscentMeters, 6);
        Assert.Equal(18, stats.DescentMeters, 6);
        Assert.Equal(35, stats.MaxElevationMeters);
        Assert.Equal(5, stats.MinElevationMeters);
    }

    [Fact]
    public void NoisyGps3k_HysteresisFiltersJitter()
    {
        var track = GpxParser.ParseFile(SamplePath("noisy-gps-3k.gpx"));
        var stats = CourseProfile.FromTrack(track).Stats;

        Assert.Equal(6, track.Points.Count(p => p.Elevation is null));
        Assert.Equal(3.0024, stats.TotalDistanceKm, 0.0005);
        Assert.Equal(30, stats.AscentMeters, 6);
        Assert.Equal(0, stats.DescentMeters, 6);
        Assert.Equal(81, stats.MaxElevationMeters);
        Assert.Equal(49, stats.MinElevationMeters);
    }

    [Fact]
    public void NoElevation5k_HasDistanceButNoElevation()
    {
        var stats = Load("no-elevation-5k.gpx").Stats;

        Assert.Equal(5.000, stats.TotalDistanceKm, OneMetreKm);
        Assert.False(stats.HasElevation);
        Assert.Equal(0, stats.AscentMeters);
    }

    [Theory]
    [InlineData("empty.gpx", "空")]
    [InlineData("not-xml.gpx", "XML")]
    [InlineData("truncated.gpx", "XML")]
    [InlineData("no-track-points.gpx", "軌跡點")]
    [InlineData("kml-file.gpx", "GPX")]
    public void InvalidSamples_ProduceFriendlyErrors(string name, string keyword)
    {
        var ex = Assert.Throws<GpxFormatException>(() => GpxParser.ParseFile(SamplePath(Path.Combine("invalid", name))));

        Assert.Contains(keyword, ex.Message);
    }
}
