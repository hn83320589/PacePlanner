using System.Globalization;

namespace PacePlanner.Core.Pacing;

/// <summary>Parses the user-typed aid station list, e.g. "5, 10.5, 21" or "5、10、15".</summary>
public static class AidStationListParser
{
    private static readonly char[] Separators = [',', '，', '、', ';', '；', ' ', '\t', '\r', '\n'];

    public static bool TryParse(string? text, out IReadOnlyList<double> kms, out string? error)
    {
        var result = new List<double>();
        foreach (var token in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var km)
                || !double.IsFinite(km) || km <= 0)
            {
                kms = [];
                error = $"補給站公里數「{token}」無效，請輸入大於 0 的數字並以逗號分隔。";
                return false;
            }

            result.Add(km);
        }

        kms = result;
        error = null;
        return true;
    }

    public static string Format(IEnumerable<double> kms) =>
        string.Join(", ", kms.Select(k => k.ToString("0.###", CultureInfo.InvariantCulture)));
}
