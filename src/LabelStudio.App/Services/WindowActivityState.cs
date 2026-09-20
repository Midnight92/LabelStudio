using LabelStudio.ViewModels.Notifications;
using Microsoft.UI.Xaml;

namespace LabelStudio.App.Services;

public sealed class WindowActivityState : IAppActivityState
{
    public bool IsForeground { get; private set; }

    public void Attach(Window window) =>
        window.Activated += (_, e) => IsForeground = e.WindowActivationState != WindowActivationState.Deactivated;
}
