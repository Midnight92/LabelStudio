using LabelStudio.Devices.Discovery;

namespace LabelStudio.Devices.Transport;

public interface ITransportFactory
{
    IPrinterTransport Create(UsbPrinterInfo printer);
}

public sealed class UsbTransportFactory : ITransportFactory
{
    public IPrinterTransport Create(UsbPrinterInfo printer) => new UsbPrintTransport(printer.DevicePath);
}
