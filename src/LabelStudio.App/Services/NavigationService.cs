using LabelStudio.App.Views;
using LabelStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace LabelStudio.App.Services;

public sealed class NavigationService : INavigationService
{
    private NavigationView? _nav;
    private Frame? _frame;
    private string? _current;

    public void Attach(NavigationView nav, Frame frame) => (_nav, _frame) = (nav, frame);

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (_nav is null || _frame is null) return;
        // Re-navigating to the current page is a no-op unless it carries a parameter (a deep link).
        if (pageKey == _current && parameter is null) return;
        _current = pageKey;
        var (page, defaultParameter) = PageFor(pageKey);
        _frame.Navigate(page, parameter ?? defaultParameter, new EntranceNavigationTransitionInfo());
        var navKey = pageKey == PageKeys.BlinkCodes ? PageKeys.Printers : pageKey;
        _nav.SelectedItem = navKey == PageKeys.Settings
            ? _nav.SettingsItem
            : _nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == navKey);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack != true) return;
        _frame.GoBack();
        _current = _frame.Content switch
        {
            PrintersPage => PageKeys.Printers,
            HomePage => PageKeys.Home,
            _ => _current,
        };
    }

    private static (Type Page, object? Parameter) PageFor(string key) => key switch
    {
        PageKeys.Home => (typeof(HomePage), null),
        PageKeys.Printers => (typeof(PrintersPage), null),
        PageKeys.BlinkCodes => (typeof(BlinkCodesPage), null),
        _ => (typeof(PlaceholderPage), key),
    };
}
