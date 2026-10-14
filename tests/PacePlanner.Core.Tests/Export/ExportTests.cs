using System.Text;
using PacePlanner.Core.Course;
using PacePlanner.Core.Export;
using PacePlanner.Core.Gpx;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Tests.Export;

public class ExportTests
{
    private static PlanReport Report(string name = "台北馬 \"破三\", 全馬", double km = RaceDistances.MarathonKm)
    {
        var points = new[] { 10.0, 30, 20 }.Select((e, i) => new TrackPoint(i * 0.01, 0, e)).ToList();
        var course = CourseProfile.FromTrack(new Track("Taipei", [points]));
        var aid = new AidStationSettings(AidStationMode.EveryNKm, 5, [], 20);
        var plan = PaceCalculator.Calculate(course, new PlanSettings(km, 3 * 3600, PaceStrategy.GradeAdjusted, 0, aid));
        return new PlanReport(name, course.Stats, plan);
    }

    [Fact]
    public void Csv_HasUtf8BomAndChineseHeader()
    {
        var bytes = CsvExporter.ToBytes(Report());

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = new UTF8Encoding(false).GetString(bytes[3..]);
        Assert.StartsWith("段,起點(km),終點(km),段距離(km),平均坡度(%),目標配速(分:秒/km)", text);
    }

    [Fact]
    public void Csv_OneRowPerSegmentAndLastCumulativeIsTarget()
    {
        var lines = CsvExporter.ToText(Report()).TrimEnd().Split("\r\n");

        Assert.Equal(1 + 43, lines.Length);
        Assert.EndsWith(",3:00:00", lines[^1]);
        Assert.StartsWith("43,42.000,42.195,0.195,", lines[^1]);
    }

    [Fact]
    public void Csv_DurationsAreHMmSsSoExcelReadsMinutesNotHours()
    {
        var fields = CsvExporter.ToText(Report()).Split("\r\n")[1].Split(',');

        Assert.Matches(@"^\d:\d\d$", fields[5]);              // pace stays m:ss
        Assert.Matches(@"^0:0\d:\d\d$", fields[6]);           // run time
        Assert.Matches(@"^0:0\d:\d\d$", fields[8]);           // segment time
        Assert.Equal(fields[8], fields[9]);                   // first cumulative equals first segment
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void Csv_EscapesFields(string value, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(value));
    }

    [Fact]
    public void Html_IsSinglePageA4PortraitWithEncodedNameTableAndChart()
    {
        var html = HtmlExporter.ToHtml(Report());

        Assert.Contains("size: A4 portrait", html);
        Assert.Contains("台北馬 &quot;破三&quot;, 全馬", html);
        Assert.Contains("<svg", html);
        Assert.Equal(43, CountOccurrences(html, "<tr class=\"seg"));
        Assert.Contains("3:00:00", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org/2000/svg", string.Empty));
    }

    [Fact]
    public void Html_LongRaceSplitsTableIntoTwoColumnsToStayOnOnePage()
    {
        var html = HtmlExporter.ToHtml(Report(km: 60));

        Assert.Equal(2, CountOccurrences(html, "<table class=\"splits\""));
    }

    [Fact]
    public void Svg_ContainsProfileKmLinesAndAidMarkers()
    {
        var report = Report();

        var svg = SvgProfileRenderer.Render(report.Plan, 700, 180);

        Assert.Contains("<polyline", svg);
        Assert.Equal(42, CountOccurrences(svg, "class=\"km\""));
        Assert.Equal(report.Plan.AidStationKms.Count, CountOccurrences(svg, "class=\"aid\""));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
