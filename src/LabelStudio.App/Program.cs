using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace LabelStudio.App;

/// <summary>
/// Single instance: two processes would both open the printer's usbprint interface and steal each other's
/// replies. A second launch (or a toast click while running) redirects its activation to the first instance.
/// </summary>
public static class Program
{
    private const string InstanceKey = "LabelStudio.Main";

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static int Main(string[] args)
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            // Block on a pool thread: an await here would resume off the STA thread.
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        main.Activated += (_, e) => App.OnRedirectedActivation(e);
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }
}
