namespace LabelStudio.Core;

public static class SafeFileName
{
    /// <summary>Maps a device serial to a file-name-safe token: ASCII letters and digits kept, everything else '_'.</summary>
    public static string From(string value) => string.Concat(value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
}
