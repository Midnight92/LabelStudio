using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Tests;

internal static class TestDevices
{
    public static (DeviceService Service, SimulatedDiscovery Discovery, ISettingsService Settings) Create(TempDir dir, params SimulatedPrinter[] printers)
    {
        var (service, discovery, settings, _) = CreateTimed(dir, printers);
        return (service, discovery, settings);
    }

    public static (DeviceService Service, SimulatedDiscovery Discovery, ISettingsService Settings, FakeTimeProvider Time) CreateTimed(
        TempDir dir, params SimulatedPrinter[] printers) =>
        CreateTimed(dir, new ConfigurationSnapshotStore(dir.File("backups")), printers);

    /// <summary>The simulated printers share the service's fake clock, so calibration timing is deterministic.</summary>
    public static (DeviceService Service, SimulatedDiscovery Discovery, ISettingsService Settings, FakeTimeProvider Time) CreateTimed(
        TempDir dir, IConfigurationSnapshotStore snapshots, params SimulatedPrinter[] printers)
    {
        var time = new FakeTimeProvider();
        foreach (var p in printers) p.Clock = time;
        var discovery = new SimulatedDiscovery(printers);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        var service = new DeviceService(
            discovery, new SimulatedTransportFactory(discovery), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(dir.File("profiles")), snapshots, settings, time, NullLogger<DeviceService>.Instance);
        return (service, discovery, settings, time);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Condition not reached within 2 s.");
    }

    /// <summary>Advances the fake clock in steps until <paramref name="task"/> completes (at most 400 steps).</summary>
    public static async Task<T> DriveAsync<T>(Task<T> task, FakeTimeProvider time, TimeSpan? step = null)
    {
        for (var i = 0; i < 400 && !task.IsCompleted; i++)
        {
            time.Advance(step ?? TimeSpan.FromMilliseconds(250));
            await Task.Delay(2);
        }
        Assert.True(task.IsCompleted, "Task did not complete while driving the fake clock.");
        return await task;
    }
}
