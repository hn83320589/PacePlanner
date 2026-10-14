using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Charts;

public readonly record struct ChartPoint(double X, double Y);

public readonly record struct PlotRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>Vertical divider at a kilometre boundary. <see cref="ShowLabel"/> thins labels on long races.</summary>
public readonly record struct KilometreLine(double X, double Km, bool ShowLabel);

public readonly record struct AidMarker(double X, double Y, double Km);

public readonly record struct ElevationTick(double Y, double ElevationMeters);

/// <summary>
/// Pure geometry of the elevation profile chart in pixel space. Both the WinUI canvas and the
/// SVG export render from this, so they always agree and the maths is unit-testable.
/// </summary>
public sealed record ProfileChartLayout(
    double Width,
    double Height,
    PlotRect PlotArea,
    bool HasElevation,
    IReadOnlyList<ChartPoint> Line,
    IReadOnlyList<KilometreLine> KilometreLines,
    IReadOnlyList<AidMarker> AidMarkers,
    IReadOnlyList<ElevationTick> ElevationTicks,
    double TotalKm)
{
    public const double MarginLeft = 44;
    public const double MarginRight = 10;
    public const double MarginTop = 10;
    public const double MarginBottom = 22;

    public static ProfileChartLayout Create(PacePlan plan, double width, double height)
    {
        var area = new PlotRect(
            MarginLeft,
            MarginTop,
            Math.Max(1, width - MarginLeft - MarginRight),
            Math.Max(1, height - MarginTop - MarginBottom));
        var totalKm = plan.Settings.DistanceKm;
        var profile = plan.Course?.Points.Where(p => p.ElevationMeters is not null).ToList() ?? [];
        var hasElevation = profile.Count > 0;

        var (minEle, maxEle) = hasElevation
            ? (profile.Min(p => p.ElevationMeters!.Value), profile.Max(p => p.ElevationMeters!.Value))
            : (0d, 0d);
        var pad = Math.Max(5, (maxEle - minEle) * 0.1);
        var low = minEle - pad;
        var high = maxEle + pad;

        double X(double km) => area.Left + area.Width * Math.Clamp(km / totalKm, 0, 1);
        double Y(double ele) => area.Bottom - area.Height * (ele - low) / (high - low);

        var line = hasElevation
            ? Downsample(profile.Select(p => new ChartPoint(X(p.DistanceKm), Y(p.ElevationMeters!.Value))).ToList(), area)
            : [];

        var labelEvery = totalKm <= 12 ? 1 : totalKm <= 25 ? 2 : 5;
        var kmLines = plan.Segments.SkipLast(1)
            .Select(s => new KilometreLine(X(s.EndKm), s.EndKm, Math.Round(s.EndKm) % labelEvery == 0))
            .ToList();

        var markers = plan.AidStationKms
            .Select(km => new AidMarker(X(km), hasElevation ? Y(plan.Course!.ElevationAt(km)!.Value) : area.Bottom, km))
            .ToList();

        var ticks = hasElevation
            ? new[] { minEle, (minEle + maxEle) / 2, maxEle }.Distinct().Select(e => new ElevationTick(Y(e), e)).ToList()
            : [];

        return new ProfileChartLayout(width, height, area, hasElevation, line, kmLines, markers, ticks, totalKm);
    }

    /// <summary>Keeps the first, min and max point of every pixel column so peaks survive.</summary>
    private static List<ChartPoint> Downsample(List<ChartPoint> points, PlotRect area)
    {
        var columns = (int)Math.Ceiling(area.Width);
        if (points.Count <= columns * 2)
        {
            return points;
        }

        var result = new List<ChartPoint>(columns * 2 + 2);
        foreach (var bucket in points.GroupBy(p => (int)(p.X - area.Left)))
        {
            var items = bucket.ToList();
            var min = items.MinBy(p => p.Y);
            var max = items.MaxBy(p => p.Y);
            result.AddRange(new[] { min, max }.Distinct().OrderBy(p => items.IndexOf(p)));
        }

        if (result[^1] != points[^1])
        {
            result.Add(points[^1]);
        }

        if (result[0] != points[0])
        {
            result.Insert(0, points[0]);
        }

        return result;
    }
}
