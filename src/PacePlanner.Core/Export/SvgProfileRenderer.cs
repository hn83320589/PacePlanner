using System.Globalization;
using System.Net;
using System.Text;
using PacePlanner.Core.Charts;
using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Export;

/// <summary>Renders <see cref="ProfileChartLayout"/> as a self-contained inline SVG.</summary>
public static class SvgProfileRenderer
{
    public static string Render(PacePlan plan, double width, double height)
    {
        var layout = ProfileChartLayout.Create(plan, width, height);
        var a = layout.PlotArea;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(width)} {N(height)}\" width=\"100%\" font-size=\"9\" font-family=\"sans-serif\">");
        sb.Append($"<rect x=\"{N(a.Left)}\" y=\"{N(a.Top)}\" width=\"{N(a.Width)}\" height=\"{N(a.Height)}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.5\"/>");

        foreach (var line in layout.KilometreLines)
        {
            sb.Append($"<line class=\"km\" x1=\"{N(line.X)}\" y1=\"{N(a.Top)}\" x2=\"{N(line.X)}\" y2=\"{N(a.Bottom)}\" stroke=\"#ccc\" stroke-width=\"0.5\"/>");
            if (line.ShowLabel)
            {
                sb.Append($"<text x=\"{N(line.X)}\" y=\"{N(a.Bottom + 11)}\" text-anchor=\"middle\" fill=\"#555\">{N(line.Km)}</text>");
            }
        }

        foreach (var tick in layout.ElevationTicks)
        {
            sb.Append($"<text x=\"{N(a.Left - 4)}\" y=\"{N(tick.Y + 3)}\" text-anchor=\"end\" fill=\"#555\">{N(tick.ElevationMeters)} m</text>");
        }

        if (layout.HasElevation)
        {
            var points = string.Join(' ', layout.Line.Select(p => $"{N(p.X)},{N(p.Y)}"));
            var area = $"{N(a.Left)},{N(a.Bottom)} {points} {N(a.Right)},{N(a.Bottom)}";
            sb.Append($"<polygon points=\"{area}\" fill=\"#dbe8f5\"/>");
            sb.Append($"<polyline points=\"{points}\" fill=\"none\" stroke=\"#2a6bb0\" stroke-width=\"1.2\"/>");
        }
        else
        {
            sb.Append($"<text x=\"{N(a.Left + a.Width / 2)}\" y=\"{N(a.Top + a.Height / 2)}\" text-anchor=\"middle\" fill=\"#888\">{WebUtility.HtmlEncode("此賽道沒有海拔資料")}</text>");
        }

        foreach (var m in layout.AidMarkers)
        {
            sb.Append($"<g class=\"aid\"><line x1=\"{N(m.X)}\" y1=\"{N(a.Top)}\" x2=\"{N(m.X)}\" y2=\"{N(a.Bottom)}\" stroke=\"#e07b00\" stroke-width=\"0.8\" stroke-dasharray=\"2,2\"/>");
            sb.Append($"<circle cx=\"{N(m.X)}\" cy=\"{N(m.Y)}\" r=\"3\" fill=\"#e07b00\"/></g>");
        }

        sb.Append($"<text x=\"{N(a.Right)}\" y=\"{N(height - 1)}\" text-anchor=\"end\" fill=\"#555\">km</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
