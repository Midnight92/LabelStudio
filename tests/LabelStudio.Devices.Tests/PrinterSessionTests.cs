using LabelStudio.Devices.Simulation;

namespace LabelStudio.Devices.Tests;

public class PrinterSessionTests
{
    private static async Task<(PrinterSession Session, SimulatedPrinter Printer)> OpenAsync()
    {
        var printer = new SimulatedPrinter();
        var session = new PrinterSession(new SimulatedPrinterTransport(printer));
        await session.OpenAsync(CancellationToken.None);
        return (session, printer);
    }

    [Fact]
    public async Task Reads_host_status()
    {
        var (session, printer) = await OpenAsync();
        printer.PaperOut = true;
        var status = await session.GetHostStatusAsync(CancellationToken.None);
        Assert.True(status.PaperOut);
    }

    [Fact]
    public async Task Reads_host_identification()
    {
        var (session, _) = await OpenAsync();
        Assert.Equal("ZD220-203dpi", (await session.GetHostIdentificationAsync(CancellationToken.None)).Model);
    }

    [Fact]
    public async Task Sgd_known_unknown_and_silent_keys()
    {
        var (session, printer) = await OpenAsync();
        printer.SilentKeys.Add("ezpl.power_up_action");
        var timeout = TimeSpan.FromMilliseconds(50);
        Assert.Equal("gap/notch", await session.GetSgdAsync("ezpl.media_type", timeout, CancellationToken.None));
        Assert.Null(await session.GetSgdAsync("not.a.key", timeout, CancellationToken.None));
        Assert.Null(await session.GetSgdAsync("ezpl.power_up_action", timeout, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_requests_are_serialised()
    {
        var (session, _) = await OpenAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            i % 2 == 0
                ? session.GetSgdAsync("ezpl.media_type", PrinterSession.DefaultTimeout, CancellationToken.None)
                : session.GetSgdAsync("head.resolution.in_dpi", PrinterSession.DefaultTimeout, CancellationToken.None)));
        for (var i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0 ? "gap/notch" : "203", results[i]);
    }

    [Fact]
    public async Task Raw_zpl_reaches_printer()
    {
        var (session, printer) = await OpenAsync();
        await session.SendRawAsync("^XA^FDhi^FS^XZ", CancellationToken.None);
        Assert.Contains("^FDhi^FS", Assert.Single(printer.ReceivedJobs));
    }

    [Fact]
    public async Task Unplugged_printer_throws_io()
    {
        var (session, printer) = await OpenAsync();
        printer.Unplugged = true;
        await Assert.ThrowsAsync<IOException>(() => session.GetHostStatusAsync(CancellationToken.None));
    }
}
