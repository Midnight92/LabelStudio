using LabelStudio.Devices.Discovery;
using LabelStudio.ViewModels.Status;

namespace LabelStudio.ViewModels.Printers;

/// <param name="Status">The current printer's status (dot + text on the row, M1 audit #6); null for other printers.</param>
public sealed record PrinterListItem(UsbPrinterInfo Info, string Name, string Serial, bool IsCurrent, StatusPresentation? Status);

public sealed record KeyValueRow(string Label, string Value);

public sealed record SgdKeyRow(string Key, string Value, bool Responded, string ResponseText, string Glyph);

/// <summary>A preflight line: colour-free status is carried by the glyph and the text (spec §15).</summary>
public sealed record ChecklistRow(string Text, bool Ok, string Glyph);
