using PacePlanner.Core.Pacing;

namespace PacePlanner.Data;

/// <summary>
/// A stored plan: course, distance, target, strategy and aid settings. The GPX text itself is
/// stored so a plan still opens after the original file is moved or deleted.
/// </summary>
public sealed record SavedPlan
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public string? GpxFileName { get; init; }

    public string? GpxContent { get; init; }

    public RaceDistance RaceDistance { get; init; } = RaceDistance.Marathon;

    public double DistanceKm { get; init; } = RaceDistances.MarathonKm;

    public int TargetSeconds { get; init; } = 4 * 3600;

    public PaceStrategy Strategy { get; init; } = PaceStrategy.Even;

    public int NegativeSplitSeconds { get; init; } = 5;

    public AidStationMode AidMode { get; init; } = AidStationMode.None;

    public double AidIntervalKm { get; init; } = 5;

    public IReadOnlyList<double> AidCustomKms { get; init; } = [];

    public int AidStopSeconds { get; init; } = 20;

    public DateTime UpdatedAt { get; init; }

    public PlanSettings ToSettings() => new(
        DistanceKm,
        TargetSeconds,
        Strategy,
        NegativeSplitSeconds,
        new AidStationSettings(AidMode, AidIntervalKm, AidCustomKms, AidStopSeconds));

    // Records compare collections by reference; compare the km list by value instead.
    public bool Equals(SavedPlan? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && GpxFileName == other.GpxFileName && GpxContent == other.GpxContent
        && RaceDistance == other.RaceDistance && DistanceKm.Equals(other.DistanceKm) && TargetSeconds == other.TargetSeconds
        && Strategy == other.Strategy && NegativeSplitSeconds == other.NegativeSplitSeconds && AidMode == other.AidMode
        && AidIntervalKm.Equals(other.AidIntervalKm) && AidCustomKms.SequenceEqual(other.AidCustomKms)
        && AidStopSeconds == other.AidStopSeconds && UpdatedAt == other.UpdatedAt;

    public override int GetHashCode() => HashCode.Combine(Id, Name, TargetSeconds, UpdatedAt);
}

public sealed record PlanSummary(long Id, string Name, DateTime UpdatedAt);
