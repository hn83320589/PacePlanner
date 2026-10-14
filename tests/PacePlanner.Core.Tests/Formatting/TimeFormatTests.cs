using PacePlanner.Core.Formatting;

namespace PacePlanner.Core.Tests.Formatting;

public class TimeFormatTests
{
    [Theory]
    [InlineData("03:30:00", 12600)]
    [InlineData("3:30:00", 12600)]
    [InlineData(" 0:25:05 ", 1505)]
    [InlineData("12:00:59", 43259)]
    public void TryParseHms_ValidInput(string text, int expected)
    {
        Assert.True(TimeFormat.TryParseHms(text, out var seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3:30")]
    [InlineData("3:60:00")]
    [InlineData("3:30:60")]
    [InlineData("a:b:c")]
    [InlineData("-1:00:00")]
    [InlineData("0:00:00")]
    public void TryParseHms_InvalidInput(string text)
    {
        Assert.False(TimeFormat.TryParseHms(text, out _));
    }

    [Theory]
    [InlineData(298.6, "4:59")]
    [InlineData(299.5, "5:00")]
    [InlineData(359.6, "6:00")]
    [InlineData(65, "1:05")]
    public void FormatPace_RoundsToWholeSecondsWithCarry(double secondsPerKm, string expected)
    {
        Assert.Equal(expected, TimeFormat.FormatPace(secondsPerKm));
    }

    [Theory]
    [InlineData(12600, "3:30:00")]
    [InlineData(59, "0:00:59")]
    [InlineData(36005, "10:00:05")]
    public void FormatDuration_IsHMmSs(int seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.FormatDuration(seconds));
    }

    [Theory]
    [InlineData(299, "4:59")]
    [InlineData(3725, "1:02:05")]
    public void FormatSplit_UsesMinutesUnderAnHour(int seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.FormatSplit(seconds));
    }
}
