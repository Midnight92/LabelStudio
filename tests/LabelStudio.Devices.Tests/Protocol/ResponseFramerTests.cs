using System.Text;
using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Tests.Protocol;

public class ResponseFramerTests
{
    private static byte[] B(string s) => Encoding.Latin1.GetBytes(s);

    [Fact]
    public void Counts_only_closed_frames()
    {
        Assert.Equal(1, ResponseFramer.CountCompleteFrames(B("\u0002a,b\u0003\r\n\u0002c")));
    }

    [Fact]
    public void Extracts_frame_contents_in_order()
    {
        var frames = ResponseFramer.ExtractFrames(B("\u0002one\u0003\r\n\u0002two\u0003\r\n"));
        Assert.Equal(["one", "two"], frames);
    }

    [Fact]
    public void Quoted_value_requires_closing_quote()
    {
        Assert.False(ResponseFramer.TryExtractQuoted(B("\"gap/no"), out _));
        Assert.True(ResponseFramer.TryExtractQuoted(B("\"gap/notch\""), out var value));
        Assert.Equal("gap/notch", value);
    }
}
