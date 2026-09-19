using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Commands;

namespace LabelStudio.Devices.Tests.Commands;

public class TestLabelTests
{
    private static CapabilityProfile Profile(string serial, IReadOnlyDictionary<string, string>? settings = null) =>
        new("ZD220-203dpi", "V84^20", 8, "8192KB", serial, PrintMethod.ThermalTransfer, settings ?? new Dictionary<string, string>(), []);

    [Fact]
    public void Is_one_complete_format_with_serial_and_host_time()
    {
        var zpl = TestLabel.Build(Profile("ABC123456789"), new DateTimeOffset(2026, 9, 19, 14, 5, 0, TimeSpan.Zero));
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl);
        Assert.Contains("ABC123456789", zpl);
        Assert.Contains("2026-09-19 14:05", zpl);
    }

    [Fact]
    public void Strips_control_prefixes_from_field_data()
    {
        var zpl = TestLabel.Build(Profile("AB~C"), DateTimeOffset.UnixEpoch);
        Assert.Contains("V8420", zpl);
        Assert.Contains("ABC", zpl);
    }

    [Fact]
    public void Compact_layout_on_small_media()
    {
        var settings = new Dictionary<string, string> { [SgdKeys.LabelLength] = "209", [SgdKeys.PrintWidth] = "320" };
        var zpl = TestLabel.Build(Profile("ABC123456789", settings), new DateTimeOffset(2026, 9, 19, 14, 5, 0, TimeSpan.Zero));
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl);
        Assert.Contains("^BY1", zpl);
        Assert.Contains("^A0N,22,22", zpl);
        Assert.Contains("ABC123456789", zpl);
        Assert.Contains("2026-09-19 14:05", zpl);
    }

    [Fact]
    public void Full_layout_when_media_is_not_small()
    {
        var zpl = TestLabel.Build(Profile("ABC123456789"), new DateTimeOffset(2026, 9, 19, 14, 5, 0, TimeSpan.Zero));
        Assert.Contains("^BY2", zpl);
    }
}
