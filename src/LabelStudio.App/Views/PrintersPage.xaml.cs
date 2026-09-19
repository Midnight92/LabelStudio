using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views;

public sealed partial class PrintersPage : Page
{
    public PrintersPage()
    {
        ViewModel = App.Services.GetRequiredService<PrintersViewModel>();
        InitializeComponent();
    }

    public PrintersViewModel ViewModel { get; }

    private async void OnPrinterClicked(object sender, ItemClickEventArgs e) =>
        await ViewModel.ConnectCommand.ExecuteAsync((PrinterListItem)e.ClickedItem);

    private void OnSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var capabilities = (string?)sender.SelectedItem?.Tag == "capabilities";
        OverviewSection.Visibility = capabilities ? Visibility.Collapsed : Visibility.Visible;
        CapabilitiesPanel.Visibility = capabilities ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Dispose();
}
