using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Tests;

internal static class TestDevices
{
    public static (DeviceService Service, SimulatedDiscovery Discovery, ISettingsService Settings) Create(TempDir dir, params SimulatedPrinter[] printers)
    {
        var discovery = new SimulatedDiscovery(printers);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        var service = new DeviceService(
            discovery, new SimulatedTransportFactory(discovery), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(dir.File("profiles")), settings, new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        return (service, discovery, settings);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Condition not reached within 2 s.");
    }
}
