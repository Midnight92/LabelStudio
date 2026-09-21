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
        _navigation.NavigateTo(PageKeys.Home);

        // The system caption buttons are drawn by the AppWindow, not by our extended title bar, and they do not
        // follow the app's theme on their own: in light mode they stay white on our light title bar, which hides
        // Minimise/Maximise/Close completely. Drive them from the content's actual theme instead, and again
        // whenever that theme changes.
        SyncCaptionButtonColours();
        if (Content is FrameworkElement root) root.ActualThemeChanged += (_, _) => SyncCaptionButtonColours();
    }

    public ShellViewModel Shell { get; }

    /// <summary>Paints the system caption buttons with the Fluent text tokens for the theme the content is using.</summary>
    private void SyncCaptionButtonColours()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = TokenColour("TextFillColorPrimaryBrush");
        bar.ButtonHoverForegroundColor = TokenColour("TextFillColorPrimaryBrush");
        bar.ButtonPressedForegroundColor = TokenColour("TextFillColorSecondaryBrush");
        bar.ButtonInactiveForegroundColor = TokenColour("TextFillColorDisabledBrush");
        bar.ButtonHoverBackgroundColor = TokenColour("SubtleFillColorSecondaryBrush");
        bar.ButtonPressedBackgroundColor = TokenColour("SubtleFillColorTertiaryBrush");
    }

    /// <returns>The platform token's colour, or null to leave that slot at its platform default.</returns>
    private static Windows.UI.Color? TokenColour(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush
            ? brush.Color
            : null;

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
