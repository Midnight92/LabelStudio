using System.Globalization;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Status;
using LabelStudio.ViewModels.Status;

namespace LabelStudio.ViewModels.Tests;

public class StatusPresenterTests
{
    private static readonly CapabilityProfile Profile = new("ZD220-203dpi", "V84", 8, "", "S1", PrintMethod.ThermalTransfer, new Dictionary<string, string>(), []);

    private static DeviceSnapshot Connected(PrinterState state) =>
        new(ConnectionState.Connected, null, Profile, null, state, DeviceProblem.None);

    [Fact]
    public void Ready_is_success_with_variant_in_pill()
    {
        var p = StatusPresenter.Present(Connected(PrinterState.Ready), 1);
        Assert.Equal(StatusTone.Success, p.Tone);
        Assert.Equal("ZD220t · Ready", p.PillText);
        Assert.Equal(StatusAction.None, p.Action);
    }

    [Fact]
    public void Media_out_names_fault_and_fix()
    {
        var p = StatusPresenter.Present(Connected(PrinterState.MediaOut), 1);
        Assert.Equal(StatusTone.Critical, p.Tone);
        Assert.Equal("Printer reported media out. Load labels and close the cover, then choose Retry.", p.Body);
        Assert.Equal(StatusAction.Retry, p.Action);
    }

    [Fact]
    public void Claimed_offers_reconnect()
    {
        var p = StatusPresenter.Present(DeviceSnapshot.Initial with { Connection = ConnectionState.Disconnected, Problem = DeviceProblem.Claimed }, 1);
        Assert.Equal(StatusTone.Critical, p.Tone);
        Assert.Equal(StatusAction.Reconnect, p.Action);
    }

    [Fact]
    public void Several_printers_unchosen_points_to_printers_page()
    {
        Assert.Equal(StatusAction.OpenPrinters, StatusPresenter.Present(DeviceSnapshot.Initial, 2).Action);
    }

    [Fact]
    public void Every_state_has_complete_localised_text()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var snapshots = Enum.GetValues<PrinterState>().Select(Connected)
            .Concat(Enum.GetValues<DeviceProblem>().Select(p => DeviceSnapshot.Initial with { Connection = ConnectionState.Disconnected, Problem = p }))
            .Append(DeviceSnapshot.Initial with { Connection = ConnectionState.Connecting })
            .Append(DeviceSnapshot.Initial);
        foreach (var s in snapshots)
        {
            var p = StatusPresenter.Present(s, 0);
            foreach (var text in new[] { p.PillText, p.Title, p.Body, p.Glyph })
                Assert.False(string.IsNullOrWhiteSpace(text) || text.StartsWith('['), $"Missing text for {s}");
            Assert.True(p.Action == StatusAction.None || p.ActionLabel is { Length: > 0 });
        }
    }
}
