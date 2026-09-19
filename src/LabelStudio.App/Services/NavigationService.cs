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

    public void NavigateTo(string pageKey)
    {
        if (_nav is null || _frame is null || pageKey == _current) return;
        _current = pageKey;
        var (page, parameter) = PageFor(pageKey);
        _frame.Navigate(page, parameter, new EntranceNavigationTransitionInfo());
        _nav.SelectedItem = pageKey == PageKeys.Settings
            ? _nav.SettingsItem
            : _nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == pageKey);
    }

    private static (Type Page, object? Parameter) PageFor(string key) => key switch
    {
        PageKeys.Printers => (typeof(PrintersPage), null),
        _ => (typeof(PlaceholderPage), key),
    };
}
