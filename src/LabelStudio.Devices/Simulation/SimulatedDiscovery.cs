using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Transport;

namespace LabelStudio.Devices.Simulation;

public sealed class SimulatedDiscovery(params SimulatedPrinter[] printers) : IPrinterDiscovery
{
    public List<SimulatedPrinter> Printers { get; } = [.. printers];
    public event EventHandler? DevicesChanged;

    public Task<IReadOnlyList<UsbPrinterInfo>> FindAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsbPrinterInfo>>(Printers.Where(p => !p.Unplugged).Select(p => p.Info).ToList());

    public void StartWatching() { }
    public void StopWatching() { }
    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class SimulatedTransportFactory(SimulatedDiscovery discovery) : ITransportFactory
{
    public IPrinterTransport Create(UsbPrinterInfo printer) =>
        new SimulatedPrinterTransport(discovery.Printers.Single(p => p.Serial == printer.Serial));
}
