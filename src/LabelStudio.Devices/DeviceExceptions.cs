namespace LabelStudio.Devices;

public sealed class PrinterProtocolException(string message) : Exception(message);

public enum PrinterUnavailableReason { NotFound, Claimed, IoFailure }

public sealed class PrinterUnavailableException(string message, PrinterUnavailableReason reason, Exception? inner = null)
    : Exception(message, inner)
{
    public PrinterUnavailableReason Reason { get; } = reason;
}

/// <summary>A pre-change configuration backup could not be written, so the change was not sent.</summary>
public sealed class ConfigurationBackupException(string message, Exception inner) : Exception(message, inner);
