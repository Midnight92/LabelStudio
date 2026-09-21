using LabelStudio.ViewModels.Home;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Dispose();
}
