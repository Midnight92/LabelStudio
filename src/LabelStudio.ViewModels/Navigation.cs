namespace LabelStudio.ViewModels;

public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
}

public interface INavigationService
{
    void NavigateTo(string pageKey);
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
}
