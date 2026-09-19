using Windows.Devices.Enumeration;

namespace LabelStudio.Devices.Discovery;

/// <summary>Enumerates the usbprint device interface (GUID_DEVINTERFACE_USBPRINT) and filters to Zebra's vendor id.</summary>
public sealed class UsbPrinterDiscovery : IPrinterDiscovery, IDisposable
{
    public const string UsbPrintInterfaceClass = "{28D78FAD-5A12-11D1-AE5B-0000F803A8C2}";
    private static readonly string Selector =
        $"System.Devices.InterfaceClassGuid:=\"{UsbPrintInterfaceClass}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";

    private DeviceWatcher? _watcher;
    private volatile bool _enumerated;

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<UsbPrinterInfo>> FindAllAsync(CancellationToken ct)
    {
        var devices = await DeviceInformation.FindAllAsync(Selector).AsTask(ct);
        var printers = new List<UsbPrinterInfo>();
        foreach (var device in devices)
        {
            if (UsbDevicePath.TryParse(device.Id, out var vid, out var pid, out var serial) && vid == UsbDevicePath.ZebraVendorId)
                printers.Add(new UsbPrinterInfo(device.Id, vid, pid, serial, device.Name));
        }
        return printers.OrderBy(p => p.Serial, StringComparer.Ordinal).ToList();
    }

    public void StartWatching()
    {
        if (_watcher is not null) return;
        _watcher = DeviceInformation.CreateWatcher(Selector);
        _watcher.Added += (_, _) => Raise();
        _watcher.Updated += (_, _) => Raise();
        _watcher.Removed += (_, _) => Raise();
        _watcher.EnumerationCompleted += (_, _) => _enumerated = true;
        _watcher.Start();
    }

    public void StopWatching()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted }) _watcher.Stop();
        _watcher = null;
        _enumerated = false;
    }

    public void Dispose() => StopWatching();

    // Suppress the burst of Added events for devices already present when the watcher starts.
    private void Raise()
    {
        if (_enumerated) DevicesChanged?.Invoke(this, EventArgs.Empty);
    }
}
