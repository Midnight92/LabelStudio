using LabelStudio.Devices.Discovery;

namespace LabelStudio.ViewModels.Printers;

public sealed record PrinterListItem(UsbPrinterInfo Info, string Name, string Serial, bool IsCurrent);

public sealed record KeyValueRow(string Label, string Value);

public sealed record SgdKeyRow(string Key, string Value, bool Responded, string ResponseText, string Glyph);

/// <summary>A preflight line: colour-free status is carried by the glyph and the text (spec §15).</summary>
public sealed record ChecklistRow(string Text, bool Ok, string Glyph);
