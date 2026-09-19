using LabelStudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LabelStudio.App.Views;

public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var key = (string)e.Parameter;
        var loader = new ResourceLoader();
        PageTitle.Text = loader.GetString($"Placeholder_{key}_Title");
        PageBody.Text = loader.GetString($"Placeholder_{key}_Body");
    }

    private void OnGoToPrinters(object sender, RoutedEventArgs e) =>
        App.Services.GetRequiredService<INavigationService>().NavigateTo(PageKeys.Printers);
}
