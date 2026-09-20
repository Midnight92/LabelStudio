using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Transport;
using LabelStudio.App.Services;
using LabelStudio.ViewModels;
using LabelStudio.ViewModels.Notifications;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;

namespace LabelStudio.App;

public partial class App : Application
{
    private MainWindow? _window;
    private static App? _current;
    private DispatcherQueue? _uiQueue;

    public App()
    {
        InitializeComponent();
        _current = this;
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        Services = ConfigureServices(DispatcherQueue.GetForCurrentThread());
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Called by Program on a background thread when a second launch or a toast click redirects here.</summary>
    public static void OnRedirectedActivation(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        var app = _current;
        app?._uiQueue?.TryEnqueue(() =>
        {
            app._window?.BringToFront();
            if (args.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification
                && args.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs toast)
                Services.GetRequiredService<AppNotificationService>().Route(toast.Arguments);
        });
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Always log, even for the fatal cases below, so the cause survives in the Debug log.
        Services.GetRequiredService<ILogger<App>>().LogError(e.Exception, "Unhandled exception reached the application boundary");

        // OutOfMemory/StackOverflow are not recoverable — let the platform terminate the process rather than
        // pretending we handled them and continuing in a corrupt state. Everything else: log and keep running,
        // same intent as CommandGuard for command bodies.
        e.Handled = e.Exception is not (OutOfMemoryException or StackOverflowException);
    }

    public static IServiceProvider Services { get; private set; } = null!;

    private static ServiceProvider ConfigureServices(DispatcherQueue uiQueue)
    {
        var services = new ServiceCollection();
        // Persisted log (M1 carry-in): %LOCALAPPDATA%\LabelStudio\logs\labelstudio-YYYYMMDD.log, one file per day, 7 kept.
        // Information and above only — the Debug provider still gets the chatty poll/probe messages.
        var fileLog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppDataPaths.LogsDirectory, "labelstudio-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
        services.AddLogging(b => b.AddDebug().AddSerilog(fileLog, dispose: true).SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISettingsService>(_ => new SettingsService(new JsonFileStore<AppSettings>(AppDataPaths.SettingsFile)));
        services.AddSingleton<IProfileCache>(_ => new ProfileCache(AppDataPaths.ProfilesDirectory));
        services.AddSingleton<IConfigurationSnapshotStore>(_ => new ConfigurationSnapshotStore(AppDataPaths.ConfigBackupsDirectory));
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
        services.AddSingleton<WindowActivityState>();
        services.AddSingleton<IAppActivityState>(sp => sp.GetRequiredService<WindowActivityState>());
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<AppNotificationService>());
        services.AddSingleton<FaultNotifier>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton(_ => new UserCounterStore(AppDataPaths.CountersFile));
        services.AddTransient<CalibrationViewModel>();
        services.AddTransient<MediaSetupViewModel>();
        services.AddTransient<CompatibilityCheckerViewModel>();
        services.AddTransient<PrintersViewModel>();
        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // First write to the file sink, which creates the log lazily: a clean run must still leave a log
        // behind, because that is what a user is asked to send when something goes wrong later.
        Services.GetRequiredService<ILogger<App>>().LogInformation(
            "Label Studio starting (simulator: {Simulator})",
            Environment.GetEnvironmentVariable("LABELSTUDIO_SIMULATOR") == "1");

        var notifications = Services.GetRequiredService<AppNotificationService>();
        notifications.LinkInvoked += (_, link) => _uiQueue?.TryEnqueue(() =>
        {
            _window?.BringToFront();
            Services.GetRequiredService<INavigationService>().NavigateTo(PageKeys.Printers, link);
        });
        notifications.Initialize();

        _window = new MainWindow();
        _window.Closed += (_, _) =>
        {
            notifications.Unregister();
            _ = DisposeDevicesAsync();
        };
        _window.Activate();

        // Cold start from a toast click.
        var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification
            && activation.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs toast)
            notifications.Route(toast.Arguments);

        // Instantiate now so it subscribes to DeviceService.SnapshotChanged before the first snapshot arrives.
        Services.GetRequiredService<FaultNotifier>();

        try
        {
            await Services.GetRequiredService<DeviceService>().StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILogger<App>>().LogError(ex, "Device service failed to start");
        }
    }

    private static async Task DisposeDevicesAsync()
    {
        try
        {
            await Services.GetRequiredService<DeviceService>().DisposeAsync();
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILogger<App>>().LogError(ex, "Closing the printer connection on exit failed");
        }
    }
}
