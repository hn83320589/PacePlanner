using PacePlanner.Core.Course;

namespace PacePlanner.Core.Pacing;

/// <summary>One row of the split table.</summary>
/// <param name="Index">1-based segment number.</param>
/// <param name="PaceSecondsPerKm">Target running pace (excluding aid stops).</param>
/// <param name="RunSeconds">Running time of the segment, whole seconds.</param>
/// <param name="AidStopSeconds">Aid-station stop time added at the end of this segment.</param>
/// <param name="CumulativeSeconds">Elapsed race time at the end of this segment, stops included.</param>
public sealed record PaceSegment(
    int Index,
    double StartKm,
    double EndKm,
    double GradePercent,
    double PaceSecondsPerKm,
    int RunSeconds,
    int AidStopSeconds,
    int CumulativeSeconds)
{
    public double DistanceKm => EndKm - StartKm;

    /// <summary>Running time plus aid stops; these sum to the cumulative column.</summary>
    public int DurationSeconds => RunSeconds + AidStopSeconds;
}

/// <param name="Course">The course scaled to the plan distance; null when planning without a course.</param>
public sealed record PacePlan(
    PlanSettings Settings,
    IReadOnlyList<PaceSegment> Segments,
    IReadOnlyList<double> AidStationKms,
    int RunningSeconds,
    CourseProfile? Course)
{
    public int TotalSeconds => Segments.Count == 0 ? 0 : Segments[^1].CumulativeSeconds;

    public int TotalAidSeconds => Segments.Sum(s => s.AidStopSeconds);
}

/// <summary>The settings cannot produce a valid plan. The message is user-facing.</summary>
public sealed class PlanningException(string message) : Exception(message);
