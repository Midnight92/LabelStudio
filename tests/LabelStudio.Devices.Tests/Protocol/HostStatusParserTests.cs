using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Tests.Protocol;

public class HostStatusParserTests
{
    [Fact]
    public void Parses_idle_ready_status()
    {
        var s = HostStatusParser.Parse(["030,0,0,1218,000,0,0,0,000,0,0,0", "001,0,0,0,1,2,6,0,00000000,1,000", "1234,0"]);
        Assert.False(s.PaperOut);
        Assert.False(s.Paused);
        Assert.Equal(1218, s.LabelLengthDots);
        Assert.False(s.HeadUp);
        Assert.True(s.ThermalTransferMode);
        Assert.Equal(0, s.LabelsRemainingInBatch);
    }

    [Fact]
    public void Parses_fault_flags()
    {
        var s = HostStatusParser.Parse(["030,1,1,1218,002,0,0,0,000,0,0,1", "001,0,1,1,1,2,6,0,00000047,1,000", "1234,0"]);
        Assert.True(s.PaperOut);
        Assert.True(s.Paused);
        Assert.Equal(2, s.FormatsInBuffer);
        Assert.True(s.OverTemperature);
        Assert.True(s.HeadUp);
        Assert.True(s.RibbonOut);
        Assert.Equal(47, s.LabelsRemainingInBatch);
    }

    [Fact]
    public void Rejects_short_response()
    {
        Assert.Throws<PrinterProtocolException>(() => HostStatusParser.Parse(["030,0,0"]));
    }
}
