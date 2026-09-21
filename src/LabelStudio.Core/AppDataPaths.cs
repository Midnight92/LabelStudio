namespace LabelStudio.Core;

public static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelStudio");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ProfilesDirectory => Path.Combine(Root, "printer-profiles");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string ConfigBackupsDirectory => Path.Combine(Root, "config-backups");
    public static string CountersFile => Path.Combine(Root, "counters.json");
}
