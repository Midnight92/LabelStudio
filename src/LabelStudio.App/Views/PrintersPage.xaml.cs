using System.ComponentModel;
using LabelStudio.ViewModels;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LabelStudio.App.Views;

public sealed partial class PrintersPage : Page
{
    public PrintersPage()
    {
        ViewModel = App.Services.GetRequiredService<PrintersViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public PrintersViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is PrinterDeepLink link) ViewModel.ApplyDeepLink(link);
        ShowTab(ViewModel.SelectedTab);
        BringSectionIntoView(ViewModel.SelectedSection);
    }

    /// <summary>M1 deferred: dispose on navigation away, not on Unloaded (which also fires on window/theme churn).</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    private async void OnPrinterClicked(object sender, ItemClickEventArgs e) =>
        await ViewModel.ConnectCommand.ExecuteAsync((PrinterListItem)e.ClickedItem);

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag && Enum.TryParse<PrinterTab>(tag, out var tab)) ViewModel.SelectedTab = tab;
    }

    private void OnResetConfirmed(object sender, RoutedEventArgs e) => ResetFlyout.Hide();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrintersViewModel.SelectedTab)) ShowTab(ViewModel.SelectedTab);
        else if (e.PropertyName == nameof(PrintersViewModel.SelectedSection)) BringSectionIntoView(ViewModel.SelectedSection);
    }

    private void ShowTab(PrinterTab tab)
    {
        var item = Tabs.Items.FirstOrDefault(i => (string)i.Tag == tab.ToString());
        if (item is not null && !ReferenceEquals(Tabs.SelectedItem, item)) Tabs.SelectedItem = item;
        OverviewSection.Visibility = tab == PrinterTab.Overview ? Visibility.Visible : Visibility.Collapsed;
        CalibrationSection.Visibility = tab == PrinterTab.Calibration ? Visibility.Visible : Visibility.Collapsed;
        CapabilitiesPanel.Visibility = tab == PrinterTab.Capabilities ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BringSectionIntoView(PrinterSection section)
    {
        if (section is PrinterSection.Checker or PrinterSection.LengthOnly) CheckerExpander.IsExpanded = section == PrinterSection.Checker;
        FrameworkElement? target = section switch
        {
            PrinterSection.StatusRemedy => StatusCard,
            PrinterSection.SmartCal => SmartCalSection,
            PrinterSection.Checker => CheckerExpander,
            PrinterSection.MediaSetup => MediaSetupSection,
            PrinterSection.LengthOnly => MediaSetupSection.LengthOnlyTarget,
            _ => null,
        };
        // After layout, so a just-shown tab has a size to scroll to.
        target?.DispatcherQueue.TryEnqueue(() => target.StartBringIntoView());
    }
}
