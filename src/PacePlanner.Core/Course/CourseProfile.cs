using PacePlanner.Core.Gpx;

namespace PacePlanner.Core.Course;

/// <summary>A point along the course: cumulative distance from the start and optional elevation.</summary>
public readonly record struct ProfilePoint(double DistanceKm, double? ElevationMeters);

public sealed record CourseStats(
    double TotalDistanceKm,
    double OriginalDistanceKm,
    double AscentMeters,
    double DescentMeters,
    double? MaxElevationMeters,
    double? MinElevationMeters)
{
    public bool HasElevation => MaxElevationMeters is not null;
}

/// <summary>Distance/elevation profile of a course, built from a GPX track.</summary>
public sealed class CourseProfile
{
    private readonly ProfilePoint[] _elevated;

    private CourseProfile(IReadOnlyList<ProfilePoint> points, double originalDistanceKm)
    {
        Points = points;
        _elevated = points.Where(p => p.ElevationMeters is not null).ToArray();
        var elevations = _elevated.Select(p => p.ElevationMeters!.Value).ToList();
        var (ascent, descent) = ElevationAnalyzer.GainAndLoss(elevations);
        Stats = new CourseStats(
            points[^1].DistanceKm,
            originalDistanceKm,
            ascent,
            descent,
            elevations.Count > 0 ? elevations.Max() : null,
            elevations.Count > 0 ? elevations.Min() : null);
    }

    public IReadOnlyList<ProfilePoint> Points { get; }

    public CourseStats Stats { get; }

    /// <summary>
    /// Builds the profile. Segments are joined in order; the gap between the end of one segment
    /// and the start of the next is not counted as distance (e.g. GPS paused).
    /// </summary>
    public static CourseProfile FromTrack(Track track)
    {
        var points = new List<ProfilePoint>(track.Points.Count);
        double distanceMeters = 0;
        foreach (var segment in track.Segments)
        {
            for (var i = 0; i < segment.Count; i++)
            {
                if (i > 0)
                {
                    var a = segment[i - 1];
                    var b = segment[i];
                    distanceMeters += GeoMath.HaversineMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                }

                points.Add(new ProfilePoint(distanceMeters / 1000, segment[i].Elevation));
            }
        }

        return new CourseProfile(points, distanceMeters / 1000);
    }

    /// <summary>
    /// Proportionally rescales distances so the course length equals <paramref name="targetKm"/>
    /// (e.g. a 42.6 km GPS trace planned as an official 42.195 km marathon).
    /// </summary>
    public CourseProfile ScaleTo(double targetKm)
    {
        if (targetKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetKm), targetKm, "目標距離必須大於 0。");
        }

        var total = Stats.TotalDistanceKm;
        if (total <= 0)
        {
            throw new InvalidOperationException("賽道總距離為 0，無法縮放（GPX 可能只有一個點）。");
        }

        var factor = targetKm / total;
        var scaled = Points.Select(p => p with { DistanceKm = p.DistanceKm * factor }).ToList();
        return new CourseProfile(scaled, Stats.OriginalDistanceKm);
    }

    /// <summary>Linearly interpolated elevation at a distance; null when the course has no elevation data.</summary>
    public double? ElevationAt(double distanceKm)
    {
        if (_elevated.Length == 0)
        {
            return null;
        }

        if (distanceKm <= _elevated[0].DistanceKm)
        {
            return _elevated[0].ElevationMeters;
        }

        if (distanceKm >= _elevated[^1].DistanceKm)
        {
            return _elevated[^1].ElevationMeters;
        }

        var hi = Array.FindIndex(_elevated, p => p.DistanceKm >= distanceKm);
        var a = _elevated[hi - 1];
        var b = _elevated[hi];
        var span = b.DistanceKm - a.DistanceKm;
        if (span <= 0)
        {
            return b.ElevationMeters;
        }

        var t = (distanceKm - a.DistanceKm) / span;
        return a.ElevationMeters!.Value + t * (b.ElevationMeters!.Value - a.ElevationMeters!.Value);
    }
}
