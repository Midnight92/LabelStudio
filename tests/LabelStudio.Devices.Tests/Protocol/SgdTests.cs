using System.Text;
using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Tests.Protocol;

public class SgdTests
{
    [Fact]
    public void Builds_getvar_command()
    {
        Assert.Equal("! U1 getvar \"ezpl.media_type\"\r\n", Encoding.ASCII.GetString(Sgd.GetVarCommand("ezpl.media_type")));
    }

    [Theory]
    [InlineData("media\" do")]
    [InlineData("")]
    [InlineData("A.B")]
    public void Rejects_unsafe_keys(string key) => Assert.Throws<ArgumentException>(() => Sgd.GetVarCommand(key));

    [Fact]
    public void Question_mark_means_unsupported()
    {
        Assert.Null(Sgd.InterpretValue("?"));
        Assert.Equal("203", Sgd.InterpretValue("203"));
    }

    [Fact]
    public void Builds_setvar_command()
    {
        Assert.Equal("! U1 setvar \"print.tone\" \"16.0\"\r\n", Encoding.ASCII.GetString(Sgd.SetVarCommand("print.tone", "16.0")));
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("line\r\nbreak")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public void Rejects_unsafe_values(string value) => Assert.Throws<ArgumentException>(() => Sgd.SetVarCommand("print.tone", value));

    [Fact]
    public void Accepts_spaces_and_slashes_in_values() =>
        Assert.Equal("! U1 setvar \"ezpl.media_type\" \"gap/notch\"\r\n", Encoding.ASCII.GetString(Sgd.SetVarCommand("ezpl.media_type", "gap/notch")));

    [Fact]
    public void Setvar_rejects_unsafe_keys() => Assert.Throws<ArgumentException>(() => Sgd.SetVarCommand("A.B", "1"));
}
