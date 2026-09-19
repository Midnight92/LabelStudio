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
}
