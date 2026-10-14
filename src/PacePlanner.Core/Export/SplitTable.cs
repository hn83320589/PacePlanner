using System.Globalization;
using PacePlanner.Core.Course;
using PacePlanner.Core.Formatting;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Export;

/// <summary>Everything an export needs: plan name, original course statistics and the computed plan.</summary>
public sealed record PlanReport(string Name, CourseStats? CourseStats, PacePlan Plan);

/// <summary>Display texts of a split row, shared by the UI grid, CSV and HTML so they never disagree.</summary>
public sealed record SplitRow(
    string Index,
    string StartKm,
    string EndKm,
    string DistanceKm,
    string Grade,
    string Pace,
    string RunTime,
    string AidStop,
    string Duration,
    string Cumulative,
    bool HasAidStation);

public static class SplitTable
{
    public static readonly string[] Headers =
    [
        "段", "起點(km)", "終點(km)", "段距離(km)", "平均坡度(%)", "目標配速(分:秒/km)",
        "跑步用時", "補給停留(秒)", "該段用時", "累計時間",
    ];

    public static SplitRow ToRow(PaceSegment s) => new(
        s.Index.ToString(CultureInfo.InvariantCulture),
        Km(s.StartKm),
        Km(s.EndKm),
        Km(s.DistanceKm),
        s.GradePercent.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
        TimeFormat.FormatPace(s.PaceSecondsPerKm),
        TimeFormat.FormatSplit(s.RunSeconds),
        s.AidStopSeconds.ToString(CultureInfo.InvariantCulture),
        TimeFormat.FormatSplit(s.DurationSeconds),
        TimeFormat.FormatDuration(s.CumulativeSeconds),
        s.AidStopSeconds > 0);

    public static IReadOnlyList<SplitRow> Rows(PacePlan plan) => plan.Segments.Select(ToRow).ToList();

    public static string StrategyName(PaceStrategy strategy) => strategy switch
    {
        PaceStrategy.Even => "等速",
        PaceStrategy.NegativeSplit => "負分段",
        PaceStrategy.GradeAdjusted => "坡度調整",
        _ => strategy.ToString(),
    };

    public static string Km(double km) => km.ToString("0.000", CultureInfo.InvariantCulture);
}
