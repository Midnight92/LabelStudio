using LabelStudio.ViewModels.Home;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    private void OnCalibrateClicked(object sender, RoutedEventArgs e) =>
        ViewModel.CalibrateCommand.Execute((sender as FrameworkElement)?.Tag as HomePrinterCard);

    // No OnNavigatedFrom disposal here, unlike PrintersPage: HomeViewModel is a singleton that owns the
    // first-run wizard's progress, so navigating away must not tear it down.
}
