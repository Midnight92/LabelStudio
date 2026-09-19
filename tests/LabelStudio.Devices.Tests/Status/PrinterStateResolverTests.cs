using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;

namespace LabelStudio.Devices.Tests.Status;

public class PrinterStateResolverTests
{
    private static HostStatus S(bool paperOut = false, bool paused = false, bool headUp = false, bool ribbonOut = false, bool tt = true, bool over = false, bool under = false)
        => new(paperOut, paused, 1218, 0, false, under, over, headUp, ribbonOut, tt, 0);

    [Fact] public void Ready_when_no_flags() => Assert.Equal(PrinterState.Ready, PrinterStateResolver.Resolve(S()));
    [Fact] public void Head_open_beats_media_out() => Assert.Equal(PrinterState.HeadOpen, PrinterStateResolver.Resolve(S(paperOut: true, paused: true, headUp: true)));
    [Fact] public void Media_out_beats_paused() => Assert.Equal(PrinterState.MediaOut, PrinterStateResolver.Resolve(S(paperOut: true, paused: true)));
    [Fact] public void Ribbon_out_only_in_thermal_transfer() => Assert.Equal(PrinterState.Ready, PrinterStateResolver.Resolve(S(ribbonOut: true, tt: false)));
    [Fact] public void Ribbon_out_reported() => Assert.Equal(PrinterState.RibbonOut, PrinterStateResolver.Resolve(S(ribbonOut: true)));
    [Fact] public void Over_temperature_reported() => Assert.Equal(PrinterState.OverTemperature, PrinterStateResolver.Resolve(S(over: true, paused: true)));
    [Fact] public void Paused_reported() => Assert.Equal(PrinterState.Paused, PrinterStateResolver.Resolve(S(paused: true)));
}
