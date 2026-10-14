using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PacePlanner.Core.Gpx;

/// <summary>
/// Reads &lt;trk&gt;/&lt;trkseg&gt;/&lt;trkpt&gt; from GPX 1.0 or 1.1. Waypoints, routes and timestamps are ignored.
/// Elements are matched by local name so both namespaced and namespace-less files work.
/// </summary>
public static class GpxParser
{
    public static Track ParseFile(string path)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new GpxFormatException($"無法讀取檔案「{path}」：{ex.Message}", ex);
        }

        return Parse(content);
    }

    public static Track Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new GpxFormatException("檔案是空的，沒有任何內容。");
        }

        var root = LoadRoot(content);
        if (root.Name.LocalName != "gpx")
        {
            throw new GpxFormatException($"這不是 GPX 檔案（根元素為 <{root.Name.LocalName}>）。");
        }

        var tracks = Children(root, "trk").ToList();
        var segments = tracks
            .SelectMany(trk => Children(trk, "trkseg"))
            .Select(seg => (IReadOnlyList<TrackPoint>)Children(seg, "trkpt").Select(ParsePoint).ToList())
            .Where(points => points.Count > 0)
            .ToList();

        if (segments.Count == 0)
        {
            throw new GpxFormatException("GPX 中沒有任何軌跡點（<trkpt>），無法建立賽道。");
        }

        var name = tracks.Select(t => Children(t, "name").FirstOrDefault()?.Value.Trim())
            .FirstOrDefault(n => !string.IsNullOrEmpty(n));
        return new Track(name, segments);
    }

    private static XElement LoadRoot(string content)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(content), settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo).Root!;
        }
        catch (XmlException ex)
        {
            throw new GpxFormatException($"檔案不是有效的 XML（第 {ex.LineNumber} 行）：{ex.Message}", ex);
        }
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName);

    private static TrackPoint ParsePoint(XElement trkpt)
    {
        var lat = ParseCoordinate(trkpt, "lat", 90);
        var lon = ParseCoordinate(trkpt, "lon", 180);
        var eleText = Children(trkpt, "ele").FirstOrDefault()?.Value;
        double? ele = double.TryParse(eleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var e) && double.IsFinite(e)
            ? e
            : null;
        return new TrackPoint(lat, lon, ele);
    }

    private static double ParseCoordinate(XElement trkpt, string attribute, double limit)
    {
        var text = trkpt.Attribute(attribute)?.Value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || Math.Abs(value) > limit)
        {
            var line = ((IXmlLineInfo)trkpt).HasLineInfo() ? $"第 {((IXmlLineInfo)trkpt).LineNumber} 行" : "某個軌跡點";
            throw new GpxFormatException($"{line}的座標 {attribute}=\"{text}\" 無效。");
        }

        return value;
    }
}
