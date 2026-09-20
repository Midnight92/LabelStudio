using LabelStudio.Devices;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels;

/// <summary>
/// Shared guard for view-model command bodies. Runs the action, maps known device-layer failures to the
/// existing localised command-error message, maps anything else to a generic one, and never lets an
/// exception escape — a command bound to a keyboard accelerator or an <c>async void</c> click handler
/// would otherwise terminate the app (spec §15 error-state conventions; this is the command-level analogue).
/// Every non-cancellation failure is also logged, so a command failure is diagnosable after the fact.
/// </summary>
internal static class CommandGuard
{
    public static async Task RunAsync(Func<CancellationToken, Task> action, Action<string?> setError, ILogger log, CancellationToken ct = default)
    {
        setError(null);
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
        {
            // Expected during app/service shutdown — not a user-facing failure, so swallow silently.
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException
            or ObjectDisposedException or PrinterUnavailableException or PrinterProtocolException)
        {
            log.LogWarning(ex, "Printer command failed");
            setError(Strings.Format("Printers.CommandError", ex.Message));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected error in a command");
            setError(Strings.Get("Command.UnexpectedError"));
        }
    }
}
