namespace PacePlanner.Core.Pacing;

public enum PaceStrategy
{
    Even = 0,
    NegativeSplit = 1,
    GradeAdjusted = 2,
}

public enum RaceDistance
{
    FiveK = 0,
    TenK = 1,
    HalfMarathon = 2,
    Marathon = 3,
    Custom = 4,
}

public static class RaceDistances
{
    public const double HalfMarathonKm = 21.0975;
    public const double MarathonKm = 42.195;

    /// <summary>Official distance in km; null for <see cref="RaceDistance.Custom"/>.</summary>
    public static double? Km(RaceDistance distance) => distance switch
    {
        RaceDistance.FiveK => 5,
        RaceDistance.TenK => 10,
        RaceDistance.HalfMarathon => HalfMarathonKm,
        RaceDistance.Marathon => MarathonKm,
        _ => null,
    };
}

public enum AidStationMode
{
    None = 0,
    EveryNKm = 1,
    CustomList = 2,
}

public sealed record AidStationSettings(
    AidStationMode Mode,
    double IntervalKm,
    IReadOnlyList<double> CustomKms,
    int StopSeconds)
{
    public static AidStationSettings None { get; } = new(AidStationMode.None, 0, [], 0);

    /// <summary>Station positions strictly inside (0, totalKm), sorted and de-duplicated.</summary>
    public IReadOnlyList<double> PositionsFor(double totalKm)
    {
        const double eps = 1e-9;
        IEnumerable<double> raw = Mode switch
        {
            AidStationMode.EveryNKm when IntervalKm > 0 =>
                Enumerable.Range(1, (int)Math.Ceiling(totalKm / IntervalKm)).Select(k => k * IntervalKm),
            AidStationMode.CustomList => CustomKms,
            _ => [],
        };
        return raw
            .Where(km => km > eps && km < totalKm - eps)
            .Select(km => Math.Round(km, 6))
            .Distinct()
            .Order()
            .ToList();
    }
}

/// <param name="DistanceKm">Planned race distance (the course is scaled to it).</param>
/// <param name="TargetSeconds">Target finish time including aid-station stops.</param>
/// <param name="NegativeSplitSeconds">How many seconds per km the second half is faster.</param>
public sealed record PlanSettings(
    double DistanceKm,
    int TargetSeconds,
    PaceStrategy Strategy,
    int NegativeSplitSeconds,
    AidStationSettings Aid);
