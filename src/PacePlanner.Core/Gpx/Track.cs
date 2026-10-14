namespace PacePlanner.Core.Gpx;

/// <summary>A single GPS sample. Elevation is in metres and may be missing.</summary>
public readonly record struct TrackPoint(double Latitude, double Longitude, double? Elevation);

/// <summary>All track segments of a GPX file, in document order.</summary>
public sealed class Track
{
    public Track(string? name, IReadOnlyList<IReadOnlyList<TrackPoint>> segments)
    {
        Name = name;
        Segments = segments;
        Points = segments.SelectMany(s => s).ToList();
    }

    public string? Name { get; }

    public IReadOnlyList<IReadOnlyList<TrackPoint>> Segments { get; }

    /// <summary>All points of all segments, flattened in order.</summary>
    public IReadOnlyList<TrackPoint> Points { get; }
}
