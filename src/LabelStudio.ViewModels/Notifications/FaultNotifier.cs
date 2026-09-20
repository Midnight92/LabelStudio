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
            var isEntry = state is { } st && st != _last && Actionable.Contains(st);
            _last = state;
            if (!isEntry) return;
            entered = state!.Value;
        }

        if (_activity.IsForeground || s.Activity == DeviceActivity.Calibrating) return;
        if (entered == PrinterState.Paused && _devices.LastAppPauseAt is { } at && _time.GetUtcNow() - at < AppPauseGrace) return;
        _notifications.Show(new FaultToast(Strings.Get($"Status.{entered}.Title"), Strings.Get($"Status.{entered}.Body"), Remedy));
    }

    public void Dispose() => _devices.SnapshotChanged -= OnSnapshotChanged;
}
