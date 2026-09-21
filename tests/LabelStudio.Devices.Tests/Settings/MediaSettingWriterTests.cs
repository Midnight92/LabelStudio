using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Models;
using LabelStudio.Devices.Settings;

namespace LabelStudio.Devices.Tests.Settings;

public class MediaSettingWriterTests
{
    private static readonly ModelTraits T = ModelCatalog.Zd220;

    [Theory]
    [InlineData(SgdKeys.Darkness, "16", "16.0")]
    [InlineData(SgdKeys.Darkness, "7.5", "7.5")]
    [InlineData(SgdKeys.LabelLength, "0209", "209")]
    [InlineData(SgdKeys.TearOff, "-10", "-10")]
    [InlineData(SgdKeys.MediaType, "GAP/NOTCH", "gap/notch")]
    public void Normalises_values(string key, string input, string expected) => Assert.Equal(expected, MediaSettingWriter.Normalise(key, input, T));

    [Theory]
    [InlineData(SgdKeys.PrintWidth, "900")]
    [InlineData(SgdKeys.LabelLength, "100")]
    [InlineData(SgdKeys.Darkness, "31")]
    [InlineData(SgdKeys.TearOff, "121")]
    public void Rejects_out_of_range_values(string key, string value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaSettingWriter.Normalise(key, value, T));

    [Theory]
    [InlineData(SgdKeys.MediaType, "web")]
    [InlineData(SgdKeys.PrintMode, "cutter")]
    [InlineData(SgdKeys.LabelLength, "abc")]
    [InlineData(SgdKeys.PrintSpeed, "4.0")]
    public void Rejects_invalid_or_unwritable_values(string key, string value) =>
        Assert.ThrowsAny<ArgumentException>(() => MediaSettingWriter.Normalise(key, value, T));

    [Theory]
    [InlineData(SgdKeys.MediaType, "continuous", "^XA^MNN^XZ")]
    [InlineData(SgdKeys.MediaType, "gap/notch", "^XA^MNY^XZ")]
    [InlineData(SgdKeys.MediaType, "mark", "^XA^MNM^XZ")]
    [InlineData(SgdKeys.PrintMode, "peel off", "^XA^MMP^XZ")]
    [InlineData(SgdKeys.PrintMethod, "direct thermal", "^XA^MTD^XZ")]
    [InlineData(SgdKeys.LabelLength, "209", "^XA^LL209^XZ")]
    [InlineData(SgdKeys.PrintWidth, "320", "^XA^PW320^XZ")]
    [InlineData(SgdKeys.Darkness, "16.0", "~SD16")]
    [InlineData(SgdKeys.TearOff, "-10", "~TA-10")]
    public void Builds_zpl_equivalents(string key, string value, string zpl) => Assert.Equal(zpl, MediaSettingWriter.ToZpl(key, value));

    [Theory]
    [InlineData("16.0", "16.0", true)]
    [InlineData("16.0", "16", true)]
    [InlineData("209", "0209", true)]
    [InlineData("gap/notch", "GAP/NOTCH", true)]
    [InlineData("16.0", "15.0", false)]
    [InlineData("16.0", null, false)]
    public void Compares_read_back_values(string expected, string? actual, bool match) => Assert.Equal(match, MediaSettingWriter.Matches(expected, actual));
}
