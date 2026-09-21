namespace LabelStudio.ViewModels;

public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
}

public interface INavigationService
{
    /// <param name="parameter">Page-specific; the Printers page accepts a <see cref="PrinterDeepLink"/>.</param>
    void NavigateTo(string pageKey, object? parameter = null);
    void GoBack();
}

public static class PageKeys
{
    public const string Home = "home";
    public const string QuickPrint = "quickprint";
    public const string Printers = "printers";
    public const string Designer = "designer";
    public const string Templates = "templates";
    public const string Queue = "queue";
    public const string Settings = "settings";
    /// <summary>Not a nav item: reached from the dashboard (spec §5), shown under Printers.</summary>
    public const string BlinkCodes = "blinkcodes";
}

public enum PrinterTab { Overview, Calibration, Capabilities }

public enum PrinterSection { None, StatusRemedy, SmartCal, MediaSetup, Checker, LengthOnly }

/// <summary>Where on the Printers page to land — used by toasts, Home's Calibrate shortcut and in-page links.</summary>
public sealed record PrinterDeepLink(PrinterTab Tab, PrinterSection Section = PrinterSection.None)
{
    public string ToArgument() => $"{Tab}/{Section}";

    public static bool TryParse(string? value, out PrinterDeepLink? link)
    {
        link = null;
        var parts = value?.Split('/');
        if (parts is not { Length: 2 } || !TryName(parts[0], out PrinterTab tab) || !TryName(parts[1], out PrinterSection section))
            return false;
        link = new PrinterDeepLink(tab, section);
        return true;
    }

    /// <summary>Names only — Enum.TryParse would also accept "1" or "7".</summary>
    private static bool TryName<T>(string text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, out value) && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal);
}
