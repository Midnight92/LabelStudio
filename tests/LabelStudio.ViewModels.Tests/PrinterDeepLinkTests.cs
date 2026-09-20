namespace LabelStudio.ViewModels.Tests;

public class PrinterDeepLinkTests
{
    [Fact]
    public void Round_trips_through_its_argument()
    {
        var link = new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.Checker);
        Assert.True(PrinterDeepLink.TryParse(link.ToArgument(), out var parsed));
        Assert.Equal(link, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Calibration")]
    [InlineData("Nope/None")]
    [InlineData("Overview/Nope")]
    [InlineData("1/0")]
    public void Rejects_malformed_arguments(string? value) => Assert.False(PrinterDeepLink.TryParse(value, out _));
}
