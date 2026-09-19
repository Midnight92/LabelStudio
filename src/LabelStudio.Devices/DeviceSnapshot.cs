using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;

namespace LabelStudio.Devices;

public enum ConnectionState { NoPrinter, Connecting, Connected, Disconnected }

public enum DeviceProblem { None, NotFound, Claimed, NotResponding, Unplugged }

public sealed record DeviceSnapshot(
    ConnectionState Connection, UsbPrinterInfo? Printer, CapabilityProfile? Profile,
    HostStatus? Status, PrinterState? State, DeviceProblem Problem)
{
    public static DeviceSnapshot Initial { get; } = new(ConnectionState.NoPrinter, null, null, null, null, DeviceProblem.None);
}
