using System.Globalization;

namespace PacePlanner.Core.Formatting;

public static class TimeFormat
{
    /// <summary>Parses "h:mm:ss" / "hh:mm:ss" into seconds. Zero and malformed values are rejected.</summary>
    public static bool TryParseHms(string? text, out int seconds)
    {
        seconds = 0;
        var parts = (text ?? string.Empty).Trim().Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            || m >= 60 || s >= 60)
        {
            return false;
        }

        seconds = h * 3600 + m * 60 + s;
        return seconds > 0;
    }

    /// <summary>Pace as "m:ss" (per km), rounded to the nearest second.</summary>
    public static string FormatPace(double secondsPerKm)
    {
        var total = (int)Math.Round(secondsPerKm, MidpointRounding.AwayFromZero);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Elapsed time as "h:mm:ss".</summary>
    public static string FormatDuration(int seconds) =>
        $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";

    /// <summary>Split time: "m:ss" under an hour, otherwise "h:mm:ss".</summary>
    public static string FormatSplit(int seconds) =>
        seconds < 3600 ? $"{seconds / 60}:{seconds % 60:00}" : FormatDuration(seconds);
}
