using System.Text;

namespace PacePlanner.Core.Export;

/// <summary>Split table as CSV, UTF-8 with BOM so Excel opens Chinese text correctly.</summary>
public static class CsvExporter
{
    public static string ToText(PlanReport report)
    {
        var sb = new StringBuilder();
        AppendLine(sb, SplitTable.Headers);
        foreach (var r in SplitTable.Rows(report.Plan))
        {
            AppendLine(sb, [r.Index, r.StartKm, r.EndKm, r.DistanceKm, r.Grade, r.Pace, r.RunTime, r.AidStop, r.Duration, r.Cumulative]);
        }

        return sb.ToString();
    }

    public static byte[] ToBytes(PlanReport report)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(ToText(report))];
    }

    public static void Save(PlanReport report, string path) => File.WriteAllBytes(path, ToBytes(report));

    public static string Escape(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    private static void AppendLine(StringBuilder sb, IEnumerable<string> fields) =>
        sb.Append(string.Join(',', fields.Select(Escape))).Append("\r\n");
}
