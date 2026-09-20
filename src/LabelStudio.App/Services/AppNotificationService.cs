using LabelStudio.ViewModels;
using LabelStudio.ViewModels.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LabelStudio.App.Services;

/// <summary>Windows App SDK app notifications for an unpackaged app (spec §5 Alerts).</summary>
public sealed class AppNotificationService(ILogger<AppNotificationService> log) : INotificationService
{
    public const string LinkArgument = "link";
    private bool _registered;

    /// <summary>Raised (on any thread) when the user clicks a toast.</summary>
    public event EventHandler<PrinterDeepLink>? LinkInvoked;

    public void Initialize()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += (_, e) => Route(e.Arguments); // must precede Register(), or a click starts a new process
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "App notifications are unavailable; fault toasts are disabled");
        }
    }

    public void Show(FaultToast toast)
    {
        if (!_registered) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument(LinkArgument, toast.Link.ToArgument())
                .AddText(toast.Title)
                .AddText(toast.Body)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Showing a fault toast failed");
        }
    }

    /// <summary>Handles a toast click from any path: in-process, cold start, or redirected activation.</summary>
    public void Route(IDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue(LinkArgument, out var value) && PrinterDeepLink.TryParse(value, out var link) && link is not null)
            LinkInvoked?.Invoke(this, link);
    }

    public void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { log.LogWarning(ex, "Unregistering app notifications failed"); }
    }
}
