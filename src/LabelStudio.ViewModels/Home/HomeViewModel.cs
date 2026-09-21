using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels.Home;

public sealed record HomePrinterCard(string Name, string Serial, StatusPresentation? Status, bool CanCalibrate);

/// <summary>
/// Spec §13 Home. M2a has the Printers region (compact status cards with a Calibrate shortcut) and the
/// first-run flow; Continue and Quick actions arrive with templates (M4).
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private readonly INavigationService _navigation;
    private readonly ILogger<HomeViewModel> _log;

    public HomeViewModel(DeviceService devices, IUiDispatcher ui, INavigationService navigation, StartupState startup,
        ISettingsService settings, FirstRunViewModel firstRun, ILogger<HomeViewModel> log)
    {
        (_devices, _ui, _navigation, _log) = (devices, ui, navigation, log);
        FirstRun = firstRun;
        ShowFirstRun = startup.IsFirstRun && !settings.Current.FirstRunCompleted;
        FirstRun.Completed += OnFirstRunCompleted;
        _devices.SnapshotChanged += OnSnapshotChanged;
        _devices.PrintersChanged += OnPrintersChanged;
        Rebuild();
    }

    public FirstRunViewModel FirstRun { get; }
    public ObservableCollection<HomePrinterCard> Printers { get; } = [];

    [ObservableProperty] public partial bool ShowFirstRun { get; set; }
    [ObservableProperty] public partial bool HasNoPrinters { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }

    [RelayCommand]
    private void Calibrate(HomePrinterCard? card)
    {
        if (card is { CanCalibrate: true })
            _navigation.NavigateTo(PageKeys.Printers, new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.SmartCal));
    }

    [RelayCommand]
    private void OpenPrinters() => _navigation.NavigateTo(PageKeys.Printers);

    [RelayCommand]
    private Task FindPrintersAsync() => CommandGuard.RunAsync(_devices.ReconnectAsync, e => CommandError = e, _log);

    private void OnFirstRunCompleted(object? sender, EventArgs e) => ShowFirstRun = false;

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(Rebuild);
    private void OnPrintersChanged(object? sender, EventArgs e) => _ui.Post(Rebuild);

    private void Rebuild()
    {
        var s = _devices.Snapshot;
        var current = s.Printer?.Serial;
        var cards = _devices.Printers.Select(p => p.Serial == current
            ? new HomePrinterCard(s.Profile?.VariantName ?? p.FriendlyName, p.Serial, StatusPresenter.Present(s, _devices.Printers.Count),
                s.Connection == ConnectionState.Connected)
            : new HomePrinterCard(p.FriendlyName, p.Serial, null, false)).ToList();
        if (!cards.SequenceEqual(Printers)) // polls every 3 s must not rebuild the cards
        {
            Printers.Clear();
            foreach (var card in cards) Printers.Add(card);
        }
        HasNoPrinters = Printers.Count == 0;
    }

    public void Dispose()
    {
        _devices.SnapshotChanged -= OnSnapshotChanged;
        _devices.PrintersChanged -= OnPrintersChanged;
        FirstRun.Completed -= OnFirstRunCompleted;
        FirstRun.Dispose();
    }
}
