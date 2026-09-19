using System.Text;
using System.Text.RegularExpressions;

namespace LabelStudio.Devices.Protocol;

public static partial class Sgd
{
    public static byte[] GetVarCommand(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException($"'{key}' is not a valid SGD key.", nameof(key));
        return Encoding.ASCII.GetBytes($"! U1 getvar \"{key}\"\r\n");
    }

    /// <summary>The printer answers "?" for keys it does not support.</summary>
    public static string? InterpretValue(string raw) => raw == "?" ? null : raw;

    [GeneratedRegex("^[a-z0-9_.]+$")]
    private static partial Regex KeyPattern();
}
