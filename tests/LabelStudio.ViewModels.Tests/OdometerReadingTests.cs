using LabelStudio.ViewModels.Printers;

namespace LabelStudio.ViewModels.Tests;

public class OdometerReadingTests
{
    [Fact]
    public void Parses_the_firmware_format()
    {
        Assert.True(OdometerReading.TryParse("33953 INCHES, 86242 CENTIMETERS", out var r));
        Assert.Equal(new OdometerReading(33953, 86242), r);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lots")]
    public void Rejects_anything_else(string? raw) => Assert.False(OdometerReading.TryParse(raw, out _));
}
