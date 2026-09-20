using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;

namespace LabelStudio.Devices;

public enum ConnectionState { NoPrinter, Connecting, Connected, Disconnected }

public enum DeviceProblem { None, NotFound, Claimed, NotResponding, Unplugged }

public enum DeviceActivity { None, Calibrating }

public sealed record DeviceSnapshot(
    ConnectionState Connection, UsbPrinterInfo? Printer, CapabilityProfile? Profile,
    HostStatus? Status, PrinterState? State, DeviceProblem Problem)
{
    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>();

    public static DeviceSnapshot Initial { get; } = new(ConnectionState.NoPrinter, null, null, null, null, DeviceProblem.None);

    /// <summary>What the app itself is doing on the printer (suppresses toasts and media-change detection).</summary>
    public DeviceActivity Activity { get; init; }

    /// <summary>Keys written this session but not yet saved with ^JU S. A new connection starts empty: a power cycle reverts them.</summary>
    public IReadOnlySet<string> PendingCommitKeys { get; init; } = NoKeys;

    /// <summary>Set by the media-change detector (Task 6); cleared by calibrating or dismissing.</summary>
    public bool CalibrationSuggested { get; init; }
}
