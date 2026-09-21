using LabelStudio.ViewModels;
using LabelStudio.ViewModels.Reference;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views;

public sealed partial class BlinkCodesPage : Page
{
    public BlinkCodesPage()
    {
        ViewModel = App.Services.GetRequiredService<BlinkCodesViewModel>();
        InitializeComponent();
    }

    public BlinkCodesViewModel ViewModel { get; }

    private void OnBack(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<INavigationService>().GoBack();
}
