using PacePlanner.Core.Gpx;

namespace PacePlanner.Core.Tests.Gpx;

public class GpxParserTests
{
    private const string Header = """<?xml version="1.0" encoding="UTF-8"?>""";

    private static Track Parse(string xml) => GpxParser.Parse(xml);

    [Fact]
    public void Parse_SingleSegment_ReadsPointsWithElevation()
    {
        var track = Parse(Header + """
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><name>Test</name><trkseg>
                <trkpt lat="25.0" lon="121.5"><ele>10.5</ele></trkpt>
                <trkpt lat="25.001" lon="121.5"><ele>12</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """);

        Assert.Equal("Test", track.Name);
        Assert.Single(track.Segments);
        Assert.Equal(2, track.Points.Count);
        Assert.Equal(new TrackPoint(25.0, 121.5, 10.5), track.Points[0]);
        Assert.Equal(12, track.Points[1].Elevation);
    }

    [Fact]
    public void Parse_MultipleSegmentsAndTracks_KeepsDocumentOrder()
    {
        var track = Parse(Header + """
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <trkseg><trkpt lat="1" lon="0"/><trkpt lat="2" lon="0"/></trkseg>
                <trkseg><trkpt lat="3" lon="0"/></trkseg>
              </trk>
              <trk>
                <trkseg><trkpt lat="4" lon="0"/><trkpt lat="5" lon="0"/></trkseg>
              </trk>
            </gpx>
            """);

        Assert.Equal(3, track.Segments.Count);
        Assert.Equal(new[] { 1.0, 2, 3, 4, 5 }, track.Points.Select(p => p.Latitude));
    }

    [Fact]
    public void Parse_Gpx10WithoutNamespace_IsSupported()
    {
        var track = Parse("""<gpx version="1.0"><trk><trkseg><trkpt lat="1" lon="2"/></trkseg></trk></gpx>""");

        Assert.Single(track.Points);
        Assert.Null(track.Points[0].Elevation);
    }

    [Fact]
    public void Parse_IgnoresWaypointsRoutesAndEmptySegments()
    {
        var track = Parse("""
            <gpx><wpt lat="9" lon="9"/><rte><rtept lat="8" lon="8"/></rte>
            <trk><trkseg/><trkseg><trkpt lat="1" lon="1"/></trkseg></trk></gpx>
            """);

        Assert.Single(track.Segments);
        Assert.Equal(1.0, track.Points.Single().Latitude);
    }

    [Fact]
    public void Parse_InvalidElevation_TreatedAsMissing()
    {
        var track = Parse("""<gpx><trk><trkseg><trkpt lat="1" lon="1"><ele>abc</ele></trkpt></trkseg></trk></gpx>""");

        Assert.Null(track.Points[0].Elevation);
    }

    [Theory]
    [InlineData("", "空")]
    [InlineData("   ", "空")]
    [InlineData("this is not xml", "XML")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"1\"", "XML")]
    [InlineData("<kml></kml>", "GPX")]
    [InlineData("<gpx><wpt lat=\"1\" lon=\"1\"/></gpx>", "軌跡點")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"abc\" lon=\"1\"/></trkseg></trk></gpx>", "座標")]
    [InlineData("<gpx><trk><trkseg><trkpt lat=\"91\" lon=\"1\"/></trkseg></trk></gpx>", "座標")]
    public void Parse_BadInput_ThrowsGpxFormatExceptionWithClearMessage(string content, string expectedKeyword)
    {
        var ex = Assert.Throws<GpxFormatException>(() => Parse(content));

        Assert.Contains(expectedKeyword, ex.Message);
    }

    [Fact]
    public void ParseFile_MissingFile_ThrowsGpxFormatException()
    {
        var ex = Assert.Throws<GpxFormatException>(() => GpxParser.ParseFile(@"Z:\nope\missing.gpx"));

        Assert.Contains("讀取", ex.Message);
    }
}
