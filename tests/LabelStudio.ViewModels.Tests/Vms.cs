using LabelStudio.Devices;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

/// <summary>Builds the Printers view model with its children the way DI does.</summary>
internal static class Vms
{
    public static PrintersViewModel Printers(DeviceService svc, TempDir dir, ILogger<PrintersViewModel>? log = null, RecordingNavigation? navigation = null)
    {
        var ui = new ImmediateDispatcher();
        return new PrintersViewModel(svc, ui, navigation ?? new RecordingNavigation(), new UserCounterStore(dir.File("counters.json")),
            new CalibrationViewModel(svc, ui, NullLogger<CalibrationViewModel>.Instance),
            new MediaSetupViewModel(svc, ui, TimeProvider.System, NullLogger<MediaSetupViewModel>.Instance),
            new CompatibilityCheckerViewModel(svc, ui),
            log ?? NullLogger<PrintersViewModel>.Instance);
    }
}
