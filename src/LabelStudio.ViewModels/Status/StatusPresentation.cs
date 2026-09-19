namespace LabelStudio.ViewModels.Status;

public enum StatusTone { Neutral, Success, Caution, Critical }

public enum StatusAction { None, Reconnect, Resume, Retry, OpenPrinters }

/// <summary>Colour (Tone) + icon (Glyph) + text for one printer state — status is never colour alone.</summary>
public sealed record StatusPresentation(StatusTone Tone, string Glyph, string PillText, string Title, string Body, StatusAction Action, string? ActionLabel);
