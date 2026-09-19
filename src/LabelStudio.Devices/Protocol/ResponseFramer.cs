using System.Text;

namespace LabelStudio.Devices.Protocol;

public delegate bool ResponseComplete(ReadOnlySpan<byte> received);

/// <summary>Frames printer responses: ~HS/~HI use STX…ETX frames, SGD answers are a double-quoted string.</summary>
public static class ResponseFramer
{
    public const byte Stx = 0x02;
    public const byte Etx = 0x03;

    public static int CountCompleteFrames(ReadOnlySpan<byte> data)
    {
        var count = 0;
        var open = false;
        foreach (var b in data)
        {
            if (b == Stx) open = true;
            else if (b == Etx && open) { count++; open = false; }
        }
        return count;
    }

    public static IReadOnlyList<string> ExtractFrames(ReadOnlySpan<byte> data)
    {
        var frames = new List<string>();
        var start = -1;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == Stx) start = i + 1;
            else if (data[i] == Etx && start >= 0)
            {
                frames.Add(Encoding.Latin1.GetString(data[start..i]));
                start = -1;
            }
        }
        return frames;
    }

    public static bool TryExtractQuoted(ReadOnlySpan<byte> data, out string value)
    {
        var open = data.IndexOf((byte)'"');
        if (open >= 0)
        {
            var close = data[(open + 1)..].IndexOf((byte)'"');
            if (close >= 0)
            {
                value = Encoding.Latin1.GetString(data.Slice(open + 1, close));
                return true;
            }
        }
        value = "";
        return false;
    }
}
