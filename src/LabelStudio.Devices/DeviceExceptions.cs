namespace LabelStudio.Devices;

public sealed class PrinterProtocolException(string message) : Exception(message);

public enum PrinterUnavailableReason { NotFound, Claimed, IoFailure }

public sealed class PrinterUnavailableException(string message, PrinterUnavailableReason reason, Exception? inner = null)
    : Exception(message, inner)
{
    public PrinterUnavailableReason Reason { get; } = reason;
}
