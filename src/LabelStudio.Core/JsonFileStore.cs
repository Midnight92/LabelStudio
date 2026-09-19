using System.Text.Json;

namespace LabelStudio.Core;

/// <summary>Reads and atomically writes one JSON document. Missing or corrupt files load as null.</summary>
public sealed class JsonFileStore<T>(string path) where T : class
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public T? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
