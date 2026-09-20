using LabelStudio.App.Services;
using LabelStudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LabelStudio.App;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;

    public MainWindow()
    {
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        InitializeComponent();
        Title = new ResourceLoader().GetString("AppTitleBar/Title");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        _navigation.Attach(Nav, ContentFrame);
        App.Services.GetRequiredService<WindowActivityState>().Attach(this);
        _navigation.NavigateTo(PageKeys.Printers); // M1 lands on Printers; Home replaces this in M2
    }

    public ShellViewModel Shell { get; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Restores and foregrounds the window (second launch, toast click).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = args.IsSettingsSelected ? PageKeys.Settings : args.SelectedItemContainer?.Tag as string;
        if (key is not null) _navigation.NavigateTo(key);
    }

    private async void OnRefreshInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Shell.RefreshCommand.ExecuteAsync(null);
    }
}
