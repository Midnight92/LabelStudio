using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.ViewModels.Status;

namespace LabelStudio.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DeviceService _devices;
    private readonly INavigationService _navigation;

    public ShellViewModel(DeviceService devices, IUiDispatcher ui, INavigationService navigation)
    {
        _devices = devices;
        _navigation = navigation;
        Status = StatusPresenter.Present(devices.Snapshot, devices.Printers.Count);
        devices.SnapshotChanged += (_, s) => ui.Post(() => Status = StatusPresenter.Present(s, _devices.Printers.Count));
    }

    [ObservableProperty]
    public partial StatusPresentation Status { get; set; }

    /// <summary>Set when a shell command fails; the shell doesn't display it yet (M1), but must never throw instead.</summary>
    [ObservableProperty]
    public partial string? CommandError { get; set; }

    [RelayCommand]
    private Task PillActionAsync() => CommandGuard.RunAsync(ct =>
    {
        if (Status.Action == StatusAction.Reconnect) return _devices.ReconnectAsync(ct);
        _navigation.NavigateTo(PageKeys.Printers);
        return Task.CompletedTask;
    }, e => CommandError = e);

    /// <summary>F5 — refresh printer status (spec §15).</summary>
    [RelayCommand]
    private Task RefreshAsync() => CommandGuard.RunAsync(ct => _devices.Snapshot.Connection == ConnectionState.Connected
        ? _devices.RefreshAsync(ct)
        : _devices.ReconnectAsync(ct), e => CommandError = e);
}
