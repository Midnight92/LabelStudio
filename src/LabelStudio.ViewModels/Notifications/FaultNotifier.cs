using LabelStudio.Devices;
using LabelStudio.Devices.Status;

namespace LabelStudio.ViewModels.Notifications;

/// <summary>
/// Spec §5 Alerts: toast only faults a person has to walk to the printer for, once per occurrence, and
/// never something the user can't act on or just caused (their own Pause, a calibration in progress),
/// or while they're already looking at the app.
/// </summary>
public sealed class FaultNotifier : IDisposable
{
    public static readonly TimeSpan AppPauseGrace = TimeSpan.FromSeconds(5);
    public static readonly IReadOnlySet<PrinterState> Actionable =
        new HashSet<PrinterState> { PrinterState.MediaOut, PrinterState.RibbonOut, PrinterState.HeadOpen, PrinterState.Paused };

    private static readonly PrinterDeepLink Remedy = new(PrinterTab.Overview, PrinterSection.StatusRemedy);

    private readonly DeviceService _devices;
    private readonly INotificationService _notifications;
    private readonly IAppActivityState _activity;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private PrinterState? _last;

    /// <summary>The state we have already toasted for, so a suppressed fault is still announced later.</summary>
    private PrinterState? _notifiedFor;

    public FaultNotifier(DeviceService devices, INotificationService notifications, IAppActivityState activity, TimeProvider time)
    {
        (_devices, _notifications, _activity, _time) = (devices, notifications, activity, time);
        _devices.SnapshotChanged += OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s)
    {
        PrinterState entered;
        lock (_lock)
        {
            var state = s.Connection == ConnectionState.Connected ? s.State : null;
            if (state != _last)
            {
                // A new occurrence: whatever we announced for the previous state no longer applies.
                _last = state;
                _notifiedFor = null;
            }
            if (state is not { } current || !Actionable.Contains(current) || _notifiedFor == current) return;
            entered = current;
        }

        // Suppression must not consume the occurrence: a fault that starts while the user is looking at the
        // app, or while we are calibrating, still has to be announced once they look away — that is exactly
        // the case toasts exist for. So these return WITHOUT recording that we notified.
        if (_activity.IsForeground || s.Activity == DeviceActivity.Calibrating) return;
        if (entered == PrinterState.Paused && _devices.LastAppPauseAt is { } at && _time.GetUtcNow() - at < AppPauseGrace) return;

        lock (_lock)
        {
            if (_notifiedFor == entered) return; // another thread got there first
            _notifiedFor = entered;
        }
        _notifications.Show(new FaultToast(Strings.Get($"Status.{entered}.Title"), Strings.Get($"Status.{entered}.Body"), Remedy));
    }

    public void Dispose() => _devices.SnapshotChanged -= OnSnapshotChanged;
}
