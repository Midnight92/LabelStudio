namespace LabelStudio.Devices.Discovery;

public interface IPrinterDiscovery
{
    Task<IReadOnlyList<UsbPrinterInfo>> FindAllAsync(CancellationToken ct);
    /// <summary>Raised (on a background thread) after a Zebra USB printer arrives, leaves or changes.</summary>
    event EventHandler? DevicesChanged;
    void StartWatching();
    void StopWatching();
}
