using System.Globalization;
using System.Resources;

namespace LabelStudio.ViewModels;

/// <summary>Localised strings produced by view models. XAML-only strings live in the App's .resw.</summary>
internal static class Strings
{
    private static readonly ResourceManager Resources = new("LabelStudio.ViewModels.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string name) => Resources.GetString(name, CultureInfo.CurrentUICulture) ?? $"[{name}]";

    public static string Format(string name, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(name), args);
}
