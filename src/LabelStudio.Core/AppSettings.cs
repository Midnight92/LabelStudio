namespace LabelStudio.Core;

public sealed record AppSettings
{
    public string? LastPrinterSerial { get; init; }
    public bool FirstRunCompleted { get; init; }
}
