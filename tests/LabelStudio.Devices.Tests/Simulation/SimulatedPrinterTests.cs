using LabelStudio.Devices.Simulation;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Devices.Tests.Simulation;

public class SimulatedPrinterTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    private static async Task<(PrinterSession, SimulatedPrinter, FakeTimeProvider)> OpenAsync()
    {
        var time = new FakeTimeProvider();
        var printer = new SimulatedPrinter { Clock = time };
        var session = new PrinterSession(new SimulatedPrinterTransport(printer));
        await session.OpenAsync(CancellationToken.None);
        return (session, printer, time);
    }

    [Fact]
    public async Task Setvar_survives_a_power_cycle_like_the_real_zd220t()
    {
        var (session, printer, _) = await OpenAsync();
        await session.SetSgdAsync("print.tone", "16.0", CancellationToken.None);
        printer.PowerCycle();
        Assert.Equal("16.0", await session.GetSgdAsync("print.tone", Short, CancellationToken.None));
    }

    /// <summary>Firmware that does drop unsaved values — the ^JU S path has to keep working for it.</summary>
    [Fact]
    public async Task Unsaved_setvar_is_lost_when_the_firmware_does_not_persist()
    {
        var (session, printer, _) = await OpenAsync();
        printer.SetvarPersistsWithoutSave = false;
        await session.SetSgdAsync("print.tone", "16.0", CancellationToken.None);
        printer.PowerCycle();
        Assert.Equal("20.0", await session.GetSgdAsync("print.tone", Short, CancellationToken.None));
    }

    [Fact]
    public async Task Saved_setvar_survives_power_cycle_even_without_persistence()
    {
        var (session, printer, _) = await OpenAsync();
        printer.SetvarPersistsWithoutSave = false;
        await session.SetSgdAsync("print.tone", "16.0", CancellationToken.None);
        await session.SendRawAsync("^XA^JUS^XZ", CancellationToken.None);
        printer.PowerCycle();
        Assert.Equal("16.0", await session.GetSgdAsync("print.tone", Short, CancellationToken.None));
    }

    [Fact]
    public async Task Read_only_keys_ignore_setvar()
    {
        var (session, printer, _) = await OpenAsync();
        printer.ReadOnlyKeys.Add("media.speed");
        await session.SetSgdAsync("media.speed", "6.0", CancellationToken.None);
        Assert.Equal("4.0", await session.GetSgdAsync("media.speed", Short, CancellationToken.None));
    }

    /// <summary>Hardware: ~HS answers Ready all the way through; the only signal is the rewritten label length.</summary>
    [Fact]
    public async Task Calibration_stays_ready_then_rewrites_the_label_length()
    {
        var (session, printer, time) = await OpenAsync();
        printer.CalibrationOutcome = new SimulatedCalibrationOutcome(FindsGap: true, LengthDots: 209);
        await session.SendRawAsync("~JC", CancellationToken.None);
        Assert.True(printer.IsCalibrating);
        var during = await session.GetHostStatusAsync(CancellationToken.None);
        Assert.False(during.Paused);
        Assert.False(during.PaperOut);
        Assert.Equal("1218", await session.GetSgdAsync("zpl.label_length", Short, CancellationToken.None));

        time.Advance(printer.CalibrationDuration);
        Assert.Equal("209", await session.GetSgdAsync("zpl.label_length", Short, CancellationToken.None));
        Assert.False(printer.IsCalibrating);
        Assert.False((await session.GetHostStatusAsync(CancellationToken.None)).Paused);
        Assert.Equal(1, printer.CalibrationRuns);
        Assert.Equal(2, printer.LabelsFed);
    }

    /// <summary>Hardware: wrong sensing feeds ~18 labels for ~20 s, then media out, with the length untouched.</summary>
    [Fact]
    public async Task Calibration_without_a_gap_feeds_then_ends_in_media_out()
    {
        var (session, printer, time) = await OpenAsync();
        printer.CalibrationOutcome = new SimulatedCalibrationOutcome(FindsGap: false, LengthDots: 0);
        await session.SendRawAsync("~JC", CancellationToken.None);

        time.Advance(printer.CalibrationDuration);
        var midway = await session.GetHostStatusAsync(CancellationToken.None);
        Assert.False(midway.PaperOut); // still searching: the failure takes far longer than a good run
        Assert.Equal("1218", await session.GetSgdAsync("zpl.label_length", Short, CancellationToken.None));

        // Measured on hardware: media out ~20 s after ~JC. Advancing to a fixed 20 s (not to
        // CalibrationDuration + FailureDuration) is what pins the simulator to the real timing.
        time.Advance(TimeSpan.FromSeconds(20) - printer.CalibrationDuration);
        Assert.True((await session.GetHostStatusAsync(CancellationToken.None)).PaperOut);
        Assert.Equal(18, printer.LabelsFed);
    }
}
