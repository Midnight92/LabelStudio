using LabelStudio.Devices.Calibration;
using LabelStudio.Devices.Status;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Devices.Tests.Calibration;

public class MediaChangeDetectorTests
{
    private static DeviceSnapshot S(PrinterState? state, DeviceActivity activity = DeviceActivity.None) =>
        new DeviceSnapshot(state is null ? ConnectionState.Disconnected : ConnectionState.Connected, null, null, null, state, DeviceProblem.None)
            with { Activity = activity };

    [Fact]
    public void Media_out_then_ready_suggests_calibration()
    {
        var d = new MediaChangeDetector(new FakeTimeProvider());
        Assert.False(d.Observe(S(PrinterState.Ready)));
        Assert.False(d.Observe(S(PrinterState.MediaOut)));
        Assert.True(d.Observe(S(PrinterState.Ready)));
        Assert.False(d.Observe(S(PrinterState.Ready)));
    }

    [Fact]
    public void Head_open_long_enough_suggests_calibration_on_close()
    {
        var time = new FakeTimeProvider();
        var d = new MediaChangeDetector(time);
        d.Observe(S(PrinterState.Ready));
        d.Observe(S(PrinterState.HeadOpen));
        time.Advance(MediaChangeDetector.HeadOpenThreshold);
        Assert.True(d.Observe(S(PrinterState.Ready)));
    }

    [Fact]
    public void Brief_head_open_does_not()
    {
        var time = new FakeTimeProvider();
        var d = new MediaChangeDetector(time);
        d.Observe(S(PrinterState.Ready));
        d.Observe(S(PrinterState.HeadOpen));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(d.Observe(S(PrinterState.Ready)));
    }

    [Fact]
    public void Transitions_during_calibration_are_ignored()
    {
        var d = new MediaChangeDetector(new FakeTimeProvider());
        d.Observe(S(PrinterState.MediaOut, DeviceActivity.Calibrating));
        Assert.False(d.Observe(S(PrinterState.Ready, DeviceActivity.Calibrating)));
        Assert.False(d.Observe(S(PrinterState.Ready)));
    }

    [Fact]
    public void Reconnecting_after_a_disconnect_does_not_count()
    {
        var d = new MediaChangeDetector(new FakeTimeProvider());
        d.Observe(S(PrinterState.MediaOut));
        d.Observe(S(null));
        Assert.False(d.Observe(S(PrinterState.Ready)));
    }
}
