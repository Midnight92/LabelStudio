using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Status;
using LabelStudio.ViewModels.Formatting;
using LabelStudio.ViewModels.Status;

namespace LabelStudio.ViewModels.Printers;

public sealed partial class PrintersViewModel : ObservableObject, IDisposable
{
    private static readonly (string Key, string Label, Func<string, int, string> Format)[] MediaFields =
    [
        (SgdKeys.MediaType, "Row.MediaType", (v, _) => v),
        (SgdKeys.PrintMethod, "Row.PrintMethod", (v, _) => v),
        (SgdKeys.PrintMode, "Row.PrintMode", (v, _) => v),
        (SgdKeys.LabelLength, "Row.LabelLength", Measurement.FormatDots),
        (SgdKeys.PrintWidth, "Row.PrintWidth", Measurement.FormatDots),
        (SgdKeys.Darkness, "Row.Darkness", (v, _) => v),
        (SgdKeys.PrintSpeed, "Row.PrintSpeed", (v, _) => Strings.Format("Format.Speed", v)),
        (SgdKeys.TearOff, "Row.TearOff", (v, _) => v),
    ];

    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private CapabilityProfile? _shownProfile;
    private string? _shownCurrentSerial;

    public PrintersViewModel(DeviceService devices, IUiDispatcher ui)
    {
        _devices = devices;
        _ui = ui;
        Status = StatusPresenter.Present(devices.Snapshot, devices.Printers.Count);
        Heading = Strings.Get("Printers.NoSelection");
        PauseLabel = Strings.Get("Printers.Pause");
        _devices.SnapshotChanged += OnSnapshotChanged;
        _devices.PrintersChanged += OnPrintersChanged;
        Apply(devices.Snapshot);
        ApplyPrinters();
    }

    public ObservableCollection<PrinterListItem> Printers { get; } = [];
    public ObservableCollection<KeyValueRow> Identity { get; } = [];
    public ObservableCollection<KeyValueRow> Media { get; } = [];
    public ObservableCollection<SgdKeyRow> ProbedKeys { get; } = [];

    [ObservableProperty] public partial StatusPresentation Status { get; set; }
    [ObservableProperty] public partial string Heading { get; set; }
    [ObservableProperty] public partial string PauseLabel { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial bool HasNoPrinters { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintTestLabelCommand), nameof(FeedCommand), nameof(TogglePauseCommand), nameof(ReprobeCommand))]
    public partial bool IsConnected { get; set; }

    [RelayCommand]
    private Task ConnectAsync(PrinterListItem? item) => item is null ? Task.CompletedTask : RunAsync(ct => _devices.ConnectAsync(item.Info, ct));

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(ct => IsConnected ? _devices.RefreshAsync(ct) : _devices.ReconnectAsync(ct));

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task PrintTestLabelAsync() => RunAsync(_devices.PrintTestLabelAsync);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task FeedAsync() => RunAsync(_devices.FeedAsync);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task TogglePauseAsync() => RunAsync(ct => IsPaused ? _devices.ResumeAsync(ct) : _devices.PauseAsync(ct));

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task ReprobeAsync() => RunAsync(_devices.ReprobeAsync);

    [RelayCommand]
    private Task StatusActionAsync() => Status.Action switch
    {
        StatusAction.Reconnect => RunAsync(_devices.ReconnectAsync),
        StatusAction.Resume => RunAsync(_devices.ResumeAsync),
        StatusAction.Retry => RunAsync(_devices.RefreshAsync),
        _ => Task.CompletedTask,
    };

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        CommandError = null;
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or PrinterUnavailableException or PrinterProtocolException)
        {
            CommandError = Strings.Format("Printers.CommandError", ex.Message);
        }
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(() => Apply(s));
    private void OnPrintersChanged(object? sender, EventArgs e) => _ui.Post(ApplyPrinters);

    private void Apply(DeviceSnapshot s)
    {
        Status = StatusPresenter.Present(s, _devices.Printers.Count);
        IsConnected = s.Connection == ConnectionState.Connected;
        IsPaused = s.State == PrinterState.Paused;
        PauseLabel = Strings.Get(IsPaused ? "Printers.Resume" : "Printers.Pause");
        Heading = s.Profile?.VariantName ?? s.Printer?.FriendlyName ?? Strings.Get("Printers.NoSelection");

        if (!ReferenceEquals(s.Profile, _shownProfile)) // polls every 3 s must not rebuild these lists
        {
            _shownProfile = s.Profile;
            Replace(Identity, s.Profile is null ? [] : IdentityRows(s.Profile));
            Replace(Media, s.Profile is null ? [] : MediaFields.Where(f => s.Profile.Supports(f.Key))
                .Select(f => new KeyValueRow(Strings.Get(f.Label), f.Format(s.Profile.Settings[f.Key], s.Profile.DotsPerMm))));
            Replace(ProbedKeys, s.Profile is null ? [] : SgdKeys.ProbeList.Select(k => s.Profile.Settings.TryGetValue(k, out var v)
                ? new SgdKeyRow(k, v, true, Strings.Get("Sgd.Responded"), "")
                : new SgdKeyRow(k, "", false, Strings.Get("Sgd.NoResponse"), "")));
        }
        if (s.Printer?.Serial != _shownCurrentSerial) ApplyPrinters();
    }

    private void ApplyPrinters()
    {
        _shownCurrentSerial = _devices.Snapshot.Printer?.Serial;
        Replace(Printers, _devices.Printers.Select(p => new PrinterListItem(p, p.FriendlyName, p.Serial, p.Serial == _shownCurrentSerial)));
        HasNoPrinters = _devices.Printers.Count == 0;
    }

    private static IEnumerable<KeyValueRow> IdentityRows(CapabilityProfile p)
    {
        yield return new(Strings.Get("Row.Model"), p.VariantName);
        yield return new(Strings.Get("Row.Serial"), p.Serial);
        yield return new(Strings.Get("Row.Firmware"), p.Firmware);
        yield return new(Strings.Get("Row.Resolution"), Strings.Format("Format.Resolution", Measurement.DotsPerInch(p.DotsPerMm), p.DotsPerMm));
        if (p.Memory.Length > 0) yield return new(Strings.Get("Row.Memory"), p.Memory);
        if (p.Get(SgdKeys.Languages) is { } language) yield return new(Strings.Get("Row.Languages"), language);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose()
    {
        _devices.SnapshotChanged -= OnSnapshotChanged;
        _devices.PrintersChanged -= OnPrintersChanged;
    }
}
