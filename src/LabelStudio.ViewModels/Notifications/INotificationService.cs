namespace LabelStudio.ViewModels.Notifications;

/// <summary>An actionable-fault toast (spec §5 Alerts). Title and body reuse the status card's text.</summary>
public sealed record FaultToast(string Title, string Body, PrinterDeepLink Link);

public interface INotificationService
{
    void Show(FaultToast toast);
}

/// <summary>Whether the main window is in the foreground — toasts are suppressed while it is.</summary>
public interface IAppActivityState
{
    bool IsForeground { get; }
}
