using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Transport;
using LabelStudio.App.Services;
using LabelStudio.ViewModels;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LabelStudio.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices(DispatcherQueue.GetForCurrentThread());
    }

    public static IServiceProvider Services { get; private set; } = null!;

    private static ServiceProvider ConfigureServices(DispatcherQueue uiQueue)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISettingsService>(_ => new SettingsService(new JsonFileStore<AppSettings>(AppDataPaths.SettingsFile)));
        services.AddSingleton<IProfileCache>(_ => new ProfileCache(AppDataPaths.ProfilesDirectory));
        services.AddSingleton(new CapabilityProber());

        // LABELSTUDIO_SIMULATOR=1 runs against an in-memory ZD220t (no hardware needed).
        if (Environment.GetEnvironmentVariable("LABELSTUDIO_SIMULATOR") == "1")
        {
            var discovery = new SimulatedDiscovery(new SimulatedPrinter());
            services.AddSingleton<IPrinterDiscovery>(discovery);
            services.AddSingleton<ITransportFactory>(new SimulatedTransportFactory(discovery));
        }
        else
        {
            services.AddSingleton<IPrinterDiscovery, UsbPrinterDiscovery>();
            services.AddSingleton<ITransportFactory, UsbTransportFactory>();
        }

        services.AddSingleton<DeviceService>();
        services.AddSingleton<IUiDispatcher>(new DispatcherQueueUiDispatcher(uiQueue));
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<PrintersViewModel>();
        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Closed += (_, _) => _ = Services.GetRequiredService<DeviceService>().DisposeAsync().AsTask();
        _window.Activate();
        try
        {
            await Services.GetRequiredService<DeviceService>().StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILogger<App>>().LogError(ex, "Device service failed to start");
        }
    }
}
