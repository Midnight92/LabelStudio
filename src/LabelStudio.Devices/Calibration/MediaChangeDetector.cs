using LabelStudio.Devices.Status;

namespace LabelStudio.Devices.Calibration;

/// <summary>
/// Zebra's guidance is to recalibrate after any media change, and nothing on the printer says so (spec §6).
/// A media change looks like: media out then recovered, or the head held open long enough to swap a roll.
/// Transitions caused by our own ~JC, or seen across a disconnect, don't count.
/// </summary>
public sealed class MediaChangeDetector(TimeProvider time)
{
    public static readonly TimeSpan HeadOpenThreshold = TimeSpan.FromSeconds(5);

    private PrinterState? _previous;
    private DateTimeOffset? _headOpenedAt;

    public bool Observe(DeviceSnapshot snapshot)
    {
        var state = snapshot.Connection == ConnectionState.Connected ? snapshot.State : null;
        if (snapshot.Activity == DeviceActivity.Calibrating || state is null)
        {
            _previous = state;
            _headOpenedAt = null;
            return false;
        }

        if (state == PrinterState.HeadOpen && _previous != PrinterState.HeadOpen) _headOpenedAt = time.GetUtcNow();
        var loaded = state is PrinterState.Ready or PrinterState.Paused;
        var suggest = loaded && (_previous == PrinterState.MediaOut
            || (_previous == PrinterState.HeadOpen && _headOpenedAt is { } opened && time.GetUtcNow() - opened >= HeadOpenThreshold));
        if (state != PrinterState.HeadOpen) _headOpenedAt = null;
        _previous = state;
        return suggest;
    }

    public void Reset()
    {
        _previous = null;
        _headOpenedAt = null;
    }
}
