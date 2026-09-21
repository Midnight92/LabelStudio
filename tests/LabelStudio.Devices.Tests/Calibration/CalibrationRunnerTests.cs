using LabelStudio.Devices.Calibration;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Models;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Devices.Tests.Calibration;

public class CalibrationRunnerTests
{
    private static async Task<(PrinterSession Session, CapabilityProfile Profile, SimulatedPrinter Printer, FakeTimeProvider Time)> OpenAsync(
        Action<SimulatedPrinter>? setup = null)
    {
        var time = new FakeTimeProvider();
        var printer = new SimulatedPrinter { Clock = time };
        setup?.Invoke(printer);
        var session = new PrinterSession(new SimulatedPrinterTransport(printer));
        await session.OpenAsync(CancellationToken.None);
        var profile = await new CapabilityProber(TimeSpan.FromMilliseconds(50)).ProbeAsync(session, "ABC123456789", new HashSet<string>(), CancellationToken.None);
        return (session, profile, printer, time);
    }

    private static Task<CalibrationResult> Run(PrinterSession s, CapabilityProfile p, FakeTimeProvider t, ModelTraits? traits = null) =>
        TestDevices.DriveAsync(new CalibrationRunner(t).RunAsync(s, p, traits ?? ModelCatalog.Zd220, null, CancellationToken.None), t);

    [Fact]
    public async Task Success_reports_the_detected_media()
    {
        var (session, profile, _, time) = await OpenAsync(p => p.CalibrationOutcome = new(FindsGap: true, LengthDots: 209));
        var result = await Run(session, profile, time);
        Assert.True(result.Succeeded);
        Assert.Equal("209", result.Detected[SgdKeys.LabelLength]);
        Assert.Equal("gap/notch", result.Detected[SgdKeys.MediaType]); // unchanged by ~JC, read back for display
    }

    /// <summary>
    /// The deadline has to be absolute. When the length answers a different value on every poll it never
    /// confirms, and a timeout that only applied while the length was unchanged would spin forever inside
    /// the exclusive session gate, blocking status polling and every other device command for the session.
    /// </summary>
    [Fact]
    public async Task A_length_that_never_settles_times_out_instead_of_looping_forever()
    {
        var (session, profile, _, time) = await OpenAsync(p => p.LabelLengthNeverSettles = true);
        var result = await Run(session, profile, time);
        Assert.False(result.Succeeded);
        Assert.Equal(CalibrationFailure.Timeout, result.Failure);
    }

    /// <summary>Wrong sensing: ~18 labels over ~20 s, then media out with the length untouched.</summary>
    [Fact]
    public async Task No_gap_found_is_reported_as_incompatible_media()
    {
        var (session, profile, printer, time) = await OpenAsync(p => p.CalibrationOutcome = new(FindsGap: false, LengthDots: 0));
        var result = await Run(session, profile, time);
        Assert.Equal(CalibrationFailure.NoGapFound, result.Failure);
        Assert.Equal(18, printer.LabelsFed);
    }

    [Theory]
    [InlineData(true, false, CalibrationFailure.HeadOpen)]
    [InlineData(false, true, CalibrationFailure.MediaOut)]
    public async Task Preflight_refuses_to_start_on_a_fault(bool headUp, bool paperOut, CalibrationFailure expected)
    {
        var (session, profile, printer, time) = await OpenAsync(p => { p.HeadUp = headUp; p.PaperOut = paperOut; });
        var result = await Run(session, profile, time);
        Assert.Equal(expected, result.Failure);
        Assert.Equal(0, printer.CalibrationRuns);
    }

    [Fact]
    public async Task Paused_printer_is_resumed_before_calibrating()
    {
        var (session, profile, printer, time) = await OpenAsync(p => p.Paused = true);
        var result = await Run(session, profile, time);
        Assert.True(result.Succeeded);
        Assert.False(printer.Paused);
    }

    /// <summary>Measured: ~HS says Ready from the first millisecond, so the runner must not call that "finished".</summary>
    [Fact]
    public async Task Ready_status_alone_does_not_end_the_run()
    {
        var (session, profile, printer, time) = await OpenAsync(p => p.CalibrationOutcome = new(FindsGap: true, LengthDots: 209));
        var result = await Run(session, profile, time);
        Assert.True(result.Succeeded);
        Assert.False(result.MeasuredNoChange);
        Assert.Equal("209", result.Detected[SgdKeys.LabelLength]);
        Assert.Equal(1, printer.CalibrationRuns);
    }

    /// <summary>Same media as last time: the length never changes, so the run ends on the timeout — not as a failure.</summary>
    [Fact]
    public async Task Unchanged_length_is_reported_as_measured_no_change()
    {
        var (session, profile, _, time) = await OpenAsync(p => p.CalibrationOutcome = new(FindsGap: true, LengthDots: 1218));
        var result = await Run(session, profile, time);
        Assert.True(result.Succeeded);
        Assert.True(result.MeasuredNoChange);
    }
}
