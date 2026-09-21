using System.Text;
using System.Text.RegularExpressions;

namespace LabelStudio.Devices.Protocol;

public static partial class Sgd
{
    public static byte[] GetVarCommand(string key)
    {
        ValidateKey(key);
        return Encoding.ASCII.GetBytes($"! U1 getvar \"{key}\"\r\n");
    }

    /// <summary>Builds <c>! U1 setvar "key" "value"</c>. The printer sends no reply to setvar.</summary>
    public static byte[] SetVarCommand(string key, string value)
    {
        ValidateKey(key);
        ValidateValue(value);
        return Encoding.ASCII.GetBytes($"! U1 setvar \"{key}\" \"{value}\"\r\n");
    }

    /// <summary>Values are printable ASCII without quotes, so they can never break out of the SGD string.</summary>
    public static void ValidateValue(string value)
    {
        foreach (var c in value)
            if (c is < ' ' or > '~' or '"')
                throw new ArgumentException($"'{value}' is not a valid SGD value.", nameof(value));
    }

    /// <summary>The printer answers "?" for keys it does not support.</summary>
    public static string? InterpretValue(string raw) => raw == "?" ? null : raw;

    private static void ValidateKey(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException($"'{key}' is not a valid SGD key.", nameof(key));
    }

    [GeneratedRegex("^[a-z0-9_.]+$")]
    private static partial Regex KeyPattern();
}
