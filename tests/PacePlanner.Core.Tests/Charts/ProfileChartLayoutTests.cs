using PacePlanner.Core.Charts;
using PacePlanner.Core.Course;
using PacePlanner.Core.Gpx;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Tests.Charts;

public class ProfileChartLayoutTests
{
    private static PacePlan Plan(double?[] elevations, double km, AidStationSettings? aid = null)
    {
        var points = elevations.Select((e, i) => new TrackPoint(i * 0.001, 0, e)).ToList();
        var course = CourseProfile.FromTrack(new Track(null, [points]));
        return PaceCalculator.Calculate(course, new PlanSettings(km, 3000, PaceStrategy.Even, 0, aid ?? AidStationSettings.None));
    }

    [Fact]
    public void Line_SpansPlotAreaAndMapsElevationToHeight()
    {
        var layout = ProfileChartLayout.Create(Plan([100, 200, 150], 10), width: 600, height: 200);

        var area = layout.PlotArea;
        Assert.Equal(area.Left, layout.Line[0].X, 6);
        Assert.Equal(area.Right, layout.Line[^1].X, 6);
        Assert.True(layout.Line[1].Y < layout.Line[0].Y); // higher elevation is drawn higher (smaller Y)
        Assert.InRange(layout.Line.Min(p => p.Y), area.Top, area.Bottom);
        Assert.InRange(layout.Line.Max(p => p.Y), area.Top, area.Bottom);
    }

    [Fact]
    public void KilometreLines_OneBetweenEachSegment()
    {
        var layout = ProfileChartLayout.Create(Plan([0, 0, 0], 10), 600, 200);

        Assert.Equal(9, layout.KilometreLines.Count);
        var area = layout.PlotArea;
        Assert.Equal(area.Left + area.Width * 0.5, layout.KilometreLines[4].X, 6);
        Assert.Equal(5, layout.KilometreLines[4].Km);
    }

    [Fact]
    public void AidMarkers_PlacedAtStationDistanceOnTheLine()
    {
        var aid = new AidStationSettings(AidStationMode.CustomList, 0, [2.5], 10);

        var layout = ProfileChartLayout.Create(Plan([100, 100, 100], 10, aid), 600, 200);

        var marker = Assert.Single(layout.AidMarkers);
        Assert.Equal(2.5, marker.Km);
        Assert.Equal(layout.PlotArea.Left + layout.PlotArea.Width * 0.25, marker.X, 6);
        Assert.Equal(layout.Line[0].Y, marker.Y, 6);
    }

    [Fact]
    public void NoElevation_ProducesEmptyLineButKeepsMarkers()
    {
        var aid = new AidStationSettings(AidStationMode.EveryNKm, 5, [], 10);

        var layout = ProfileChartLayout.Create(Plan([null, null, null], 10, aid), 600, 200);

        Assert.False(layout.HasElevation);
        Assert.Empty(layout.Line);
        Assert.Single(layout.AidMarkers);
    }

    [Fact]
    public void ManyPoints_AreDownsampledToPlotWidth()
    {
        var elevations = Enumerable.Range(0, 5000).Select(i => (double?)(i % 50)).ToArray();

        var layout = ProfileChartLayout.Create(Plan(elevations, 42.195), 400, 150);

        Assert.InRange(layout.Line.Count, 2, 2 * 400 + 2);
    }
}
