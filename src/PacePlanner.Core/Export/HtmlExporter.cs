using System.Globalization;
using System.Net;
using System.Text;
using PacePlanner.Core.Formatting;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Export;

/// <summary>
/// Printable, self-contained pace card: one A4 portrait page with summary, profile chart and splits.
/// Races with many segments are split into two side-by-side tables to stay on one page.
/// </summary>
public static class HtmlExporter
{
    /// <summary>Rows that fit in a single table column on one A4 page alongside the chart.</summary>
    public const int RowsPerColumn = 45;

    public static void Save(PlanReport report, string path) =>
        File.WriteAllText(path, ToHtml(report), new UTF8Encoding(false));

    public static string ToHtml(PlanReport report)
    {
        var plan = report.Plan;
        var s = plan.Settings;
        var rows = SplitTable.Rows(plan);
        var columns = rows.Count > RowsPerColumn ? 2 : 1;
        var perColumn = (int)Math.Ceiling(rows.Count / (double)columns);
        var fontPt = rows.Count > 2 * RowsPerColumn * 0.9 ? 6.5 : rows.Count > 30 ? 8 : 9;

        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="zh-Hant">
            <head>
            <meta charset="utf-8">
            <title>{{E(report.Name)}} 配速表</title>
            <style>
            @page { size: A4 portrait; margin: 8mm; }
            * { box-sizing: border-box; }
            body { font-family: "Microsoft JhengHei", "Noto Sans TC", sans-serif; margin: 0; color: #111; font-size: {{fontPt.ToString(CultureInfo.InvariantCulture)}}pt; }
            .page { width: 194mm; max-height: 281mm; overflow: hidden; margin: 0 auto; }
            h1 { font-size: 14pt; margin: 0 0 1mm; }
            .summary { display: flex; flex-wrap: wrap; gap: 0 5mm; margin-bottom: 1.5mm; }
            .chart { height: 40mm; margin-bottom: 1.5mm; }
            .chart svg { height: 40mm; }
            .tables { display: flex; gap: 3mm; align-items: flex-start; }
            table.splits { border-collapse: collapse; flex: 1; }
            th, td { border: 0.3pt solid #888; padding: 0.3mm 1mm; text-align: right; white-space: nowrap; line-height: 1.15; }
            th { background: #eee; font-weight: bold; }
            tr.aid td { background: #fff1de; }
            td.pace { font-weight: bold; }
            .legend { margin-top: 1mm; color: #555; }
            @media print { .page { page-break-after: avoid; page-break-inside: avoid; } }
            </style>
            </head>
            <body>
            <div class="page">
            <h1>{{E(report.Name)}}</h1>
            <div class="summary">
            """);

        Item(sb, "距離", $"{SplitTable.Km(s.DistanceKm)} km");
        Item(sb, "目標", TimeFormat.FormatDuration(s.TargetSeconds));
        Item(sb, "策略", SplitTable.StrategyName(s.Strategy) + (s.Strategy == PaceStrategy.NegativeSplit ? $"（後半快 {s.NegativeSplitSeconds} 秒/km）" : string.Empty));
        Item(sb, "平均配速", TimeFormat.FormatPace(plan.RunningSeconds / s.DistanceKm) + " /km（不含補給）");
        if (report.CourseStats is { } stats)
        {
            Item(sb, "爬升/下降", $"+{stats.AscentMeters:0} / -{stats.DescentMeters:0} m");
        }

        Item(sb, "補給", plan.AidStationKms.Count == 0
            ? "無"
            : $"{plan.AidStationKms.Count} 站 × {s.Aid.StopSeconds} 秒（{string.Join("、", plan.AidStationKms.Select(k => k.ToString("0.##", CultureInfo.InvariantCulture)))} km）");

        sb.Append("</div>\n<div class=\"chart\">");
        sb.Append(SvgProfileRenderer.Render(plan, 730, 150));
        sb.Append("</div>\n<div class=\"tables\">\n");

        for (var c = 0; c < columns; c++)
        {
            sb.Append("<table class=\"splits\"><thead><tr>");
            foreach (var h in new[] { "段", "起訖 km", "距離", "坡度%", "配速", "用時", "補給", "累計" })
            {
                sb.Append($"<th>{E(h)}</th>");
            }

            sb.Append("</tr></thead><tbody>\n");
            foreach (var r in rows.Skip(c * perColumn).Take(perColumn))
            {
                sb.Append($"<tr class=\"seg{(r.HasAidStation ? " aid" : string.Empty)}\">")
                    .Append($"<td>{r.Index}</td><td>{r.StartKm}–{r.EndKm}</td><td>{r.DistanceKm}</td><td>{r.Grade}</td>")
                    .Append($"<td class=\"pace\">{r.Pace}</td><td>{r.Duration}</td><td>{(r.HasAidStation ? "+" + r.AidStop + "s" : string.Empty)}</td><td>{r.Cumulative}</td></tr>\n");
            }

            sb.Append("</tbody></table>\n");
        }

        sb.Append("</div>\n<div class=\"legend\">配速＝跑步配速（分:秒/km）；用時含該段補給停留；橘色列為有補給站的路段。</div>\n</div>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Item(StringBuilder sb, string label, string value) =>
        sb.Append($"<div><b>{E(label)}</b> {E(value)}</div>\n");

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
