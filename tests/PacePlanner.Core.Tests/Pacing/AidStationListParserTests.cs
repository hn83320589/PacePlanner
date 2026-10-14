using PacePlanner.Core.Pacing;

namespace PacePlanner.Core.Tests.Pacing;

public class AidStationListParserTests
{
    [Theory]
    [InlineData("5, 10.5, 21", new[] { 5, 10.5, 21 })]
    [InlineData("5、10；15 20", new[] { 5.0, 10, 15, 20 })]
    [InlineData("", new double[0])]
    [InlineData("  ", new double[0])]
    public void TryParse_AcceptsCommonSeparators(string text, double[] expected)
    {
        Assert.True(AidStationListParser.TryParse(text, out var kms, out var error));
        Assert.Null(error);
        Assert.Equal(expected, kms);
    }

    [Theory]
    [InlineData("5, abc")]
    [InlineData("-3")]
    public void TryParse_RejectsInvalidValuesWithMessage(string text)
    {
        Assert.False(AidStationListParser.TryParse(text, out _, out var error));
        Assert.Contains("補給", error);
    }

    [Fact]
    public void Format_RoundTrips()
    {
        Assert.Equal("5, 10.5, 21", AidStationListParser.Format([5, 10.5, 21]));
    }
}
