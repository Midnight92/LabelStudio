using System.Text;
using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Transport;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.ViewModels.Tests;

public class PrintersViewModelTests
{
    /// <summary>Wraps a real simulated transport but fails a specific command with an exception type the
    /// command guard doesn't special-case, to exercise the generic "unexpected exception" mapping deterministically.</summary>
    private sealed class WriteThrowsTransport(SimulatedPrinter printer, string triggerSubstring) : IPrinterTransport
    {
        private readonly SimulatedPrinterTransport _inner = new(printer);

        public Task OpenAsync(CancellationToken ct) => _inner.OpenAsync(ct);

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
            Encoding.UTF8.GetString(data.Span).Contains(triggerSubstring, StringComparison.Ordinal)
                ? throw new NotSupportedException("Simulated unexpected transport failure.")
                : _inner.WriteAsync(data, ct);

        public Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct) => _inner.ReadAsync(buffer, timeout, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class WriteThrowsTransportFactory(SimulatedPrinter printer, string triggerSubstring) : ITransportFactory
    {
        public IPrinterTransport Create(UsbPrinterInfo printerInfo) => new WriteThrowsTransport(printer, triggerSubstring);
    }

    [Fact]
    public async Task Shows_identity_and_only_responding_media_settings()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        Assert.True(vm.IsConnected);
        Assert.Equal("ZD220t", vm.Heading);
        Assert.Contains(vm.Identity, r => r.Value == printer.Serial);
        Assert.Contains(vm.Media, r => r.Value == "gap/notch");
        Assert.DoesNotContain(vm.Media, r => r.Label == "Tear-off position"); // simulator answers "?" → hidden, not broken
        Assert.Contains(vm.ProbedKeys, k => k.Key == SgdKeys.TearOff && !k.Responded);
        Assert.Single(vm.Printers);
    }

    [Fact]
    public async Task Probed_key_rows_have_distinct_nonempty_glyphs()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        Assert.NotEmpty(vm.ProbedKeys);
        Assert.All(vm.ProbedKeys, k => Assert.False(string.IsNullOrEmpty(k.Glyph)));
        var respondedGlyph = vm.ProbedKeys.First(k => k.Responded).Glyph;
        var noResponseGlyph = vm.ProbedKeys.First(k => !k.Responded).Glyph;
        Assert.NotEqual(respondedGlyph, noResponseGlyph);
    }

    [Fact]
    public async Task Test_label_command_prints()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);
        await vm.PrintTestLabelCommand.ExecuteAsync(null);
        Assert.Single(printer.ReceivedJobs);
        Assert.Null(vm.CommandError);
    }

    [Fact]
    public async Task No_printers_shows_empty_state()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);
        Assert.True(vm.HasNoPrinters);
        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.False(vm.FeedCommand.CanExecute(null));
        Assert.Equal("Connect a printer to use these actions.", vm.ActionHint);
    }

    [Fact]
    public async Task Fault_disables_print_and_feed_but_not_reprobe()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        printer.PaperOut = true;
        await svc.RefreshAsync(CancellationToken.None);

        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.False(vm.FeedCommand.CanExecute(null));
        Assert.True(vm.ReprobeCommand.CanExecute(null));
        Assert.Equal("Clear the printer fault to print or feed.", vm.ActionHint);
    }

    [Fact]
    public async Task Paused_allows_feed_but_not_print()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        printer.Paused = true;
        await svc.RefreshAsync(CancellationToken.None);

        Assert.True(vm.FeedCommand.CanExecute(null));
        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.Equal("Resume the printer to print a test label.", vm.ActionHint);
    }

    [Fact]
    public async Task Ready_allows_print_and_feed_with_no_hint()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        Assert.True(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.True(vm.FeedCommand.CanExecute(null));
        Assert.Null(vm.ActionHint);
    }

    [Fact]
    public async Task Connecting_state_reports_IsConnecting()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        var seen = new List<bool>();
        svc.SnapshotChanged += (_, _) => seen.Add(vm.IsConnecting);

        await svc.StartAsync(CancellationToken.None);

        Assert.Contains(true, seen);
        Assert.False(vm.IsConnecting);
    }

    [Fact]
    public async Task Unexpected_exception_from_a_command_maps_to_the_generic_error()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var discovery = new SimulatedDiscovery(printer);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        var svc = new DeviceService(
            discovery, new WriteThrowsTransportFactory(printer, "~PH"), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(dir.File("profiles")), new ConfigurationSnapshotStore(dir.File("backups")), settings,
            new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);
        Assert.True(vm.IsConnected); // connect itself doesn't touch "~PH", so it should succeed normally

        await vm.FeedCommand.ExecuteAsync(null); // Feed sends "~PH", which this transport throws NotSupportedException on

        Assert.Equal("Something went wrong. Try again; if it keeps happening, restart the app.", vm.CommandError);
    }

    [Fact]
    public async Task Fault_reports_paused_because_the_printer_pauses_itself_on_faults()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        printer.PaperOut = true; // the simulator's ~HS reports Paused=true whenever PaperOut is true, like real firmware
        await svc.RefreshAsync(CancellationToken.None);

        Assert.True(vm.IsPaused);
        Assert.Equal("Resume", vm.PauseLabel);
    }

    [Fact]
    public async Task Unexpected_command_failure_is_logged_as_error()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var discovery = new SimulatedDiscovery(printer);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        await using var svc = new DeviceService(discovery, new WriteThrowsTransportFactory(printer, "~PH"),
            new CapabilityProber(TimeSpan.FromMilliseconds(50)), new ProfileCache(dir.File("profiles")),
            new ConfigurationSnapshotStore(dir.File("backups")), settings,
            new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        var log = new RecordingLogger<PrintersViewModel>();
        using var vm = Vms.Printers(svc, dir, log);
        await svc.StartAsync(CancellationToken.None);

        await vm.FeedCommand.ExecuteAsync(null);

        Assert.NotNull(vm.CommandError);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.IsType<NotSupportedException>(entry.Exception);
    }

    [Fact]
    public async Task Device_failure_is_logged_as_warning()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        var log = new RecordingLogger<PrintersViewModel>();
        using var vm = Vms.Printers(svc, dir, log);
        await svc.StartAsync(CancellationToken.None);
        printer.Unplugged = true;

        await vm.FeedCommand.ExecuteAsync(null);

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task Counters_show_printer_readings_and_an_app_counter_that_resets()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        Assert.True(vm.HasCounters);
        Assert.Contains(vm.Counters, r => r.Value.Contains(4800.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal));
        Assert.Equal("0", vm.AppCounterText);       // baseline taken on first sight

        printer.Sgd[SgdKeys.OdometerUserLabels] = "1225";
        await vm.RefreshCountersCommand.ExecuteAsync(null);
        Assert.Equal("25", vm.AppCounterText);

        vm.ResetCounterCommand.Execute(null);
        Assert.Equal("0", vm.AppCounterText);
    }

    [Fact]
    public async Task Deep_links_and_child_requests_select_tab_and_section()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);

        vm.ApplyDeepLink(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.SmartCal));
        Assert.Equal(PrinterTab.Calibration, vm.SelectedTab);
        Assert.Equal(PrinterSection.SmartCal, vm.SelectedSection);

        vm.EditMediaCommand.Execute(null);
        Assert.Equal(PrinterSection.MediaSetup, vm.SelectedSection);

        vm.Calibration.CheckCompatibilityCommand.Execute(null);
        Assert.Equal(PrinterSection.Checker, vm.SelectedSection);

        vm.Checker.UseLengthOnlyCommand.Execute(null);
        Assert.Equal(PrinterSection.LengthOnly, vm.SelectedSection);
    }

    [Fact]
    public async Task Blink_codes_link_navigates()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        var navigation = new RecordingNavigation();
        using var vm = Vms.Printers(svc, dir, navigation: navigation);
        vm.OpenBlinkCodesCommand.Execute(null);
        Assert.Equal([PageKeys.BlinkCodes], navigation.Visited);
    }

    [Fact]
    public async Task Current_printer_row_carries_its_status()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = Vms.Printers(svc, dir);
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal(StatusTone.Success, Assert.Single(vm.Printers).Status!.Tone);

        printer.PaperOut = true;
        await svc.RefreshAsync(CancellationToken.None);
        Assert.Equal(StatusTone.Critical, Assert.Single(vm.Printers).Status!.Tone);
    }
}
