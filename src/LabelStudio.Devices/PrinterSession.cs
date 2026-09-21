using System.Buffers;
using System.Diagnostics;
using System.Text;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.Devices;

/// <summary>Owns one printer connection and serialises every command/response exchange through a single gate.</summary>
public sealed class PrinterSession : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ReadSlice = TimeSpan.FromMilliseconds(200);

    private readonly IPrinterTransport _transport;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PrinterSession(IPrinterTransport transport, ILogger? log = null)
    {
        _transport = transport;
        _log = log ?? NullLogger.Instance;
    }

    public Task OpenAsync(CancellationToken ct) => _transport.OpenAsync(ct);

    public async Task<HostStatus> GetHostStatusAsync(CancellationToken ct)
    {
        var bytes = await ExchangeAsync(Encoding.ASCII.GetBytes("~HS"), d => ResponseFramer.CountCompleteFrames(d) >= 3, DefaultTimeout, ct);
        return HostStatusParser.Parse(ResponseFramer.ExtractFrames(bytes));
    }

    public async Task<HostIdentification> GetHostIdentificationAsync(CancellationToken ct)
    {
        var bytes = await ExchangeAsync(Encoding.ASCII.GetBytes("~HI"), d => ResponseFramer.CountCompleteFrames(d) >= 1, DefaultTimeout, ct);
        return HostIdentificationParser.Parse(ResponseFramer.ExtractFrames(bytes)[0]);
    }

    /// <returns>The value, or null when the printer answers "?" or does not answer at all.</returns>
    public async Task<string?> GetSgdAsync(string key, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var bytes = await ExchangeAsync(Sgd.GetVarCommand(key), d => ResponseFramer.TryExtractQuoted(d, out _), timeout, ct);
            ResponseFramer.TryExtractQuoted(bytes, out var value);
            return Sgd.InterpretValue(value);
        }
        catch (TimeoutException)
        {
            _log.LogDebug("SGD key {Key} did not respond", key);
            return null;
        }
    }

    /// <summary>Sends an SGD setvar. The printer does not reply, so callers read the key back to verify.</summary>
    public async Task SetSgdAsync(string key, string value, CancellationToken ct)
    {
        var command = Sgd.SetVarCommand(key, value);
        await _gate.WaitAsync(ct);
        try { await _transport.WriteAsync(command, ct); }
        finally { _gate.Release(); }
    }

    public async Task SendRawAsync(string commands, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await _transport.WriteAsync(Encoding.UTF8.GetBytes(commands), ct); }
        finally { _gate.Release(); }
    }

    private async Task<byte[]> ExchangeAsync(byte[] request, ResponseComplete isComplete, TimeSpan timeout, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await DrainAsync(ct);
            await _transport.WriteAsync(request, ct);
            var received = new ArrayBufferWriter<byte>();
            var chunk = new byte[1024];
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                var remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"No complete response to '{Encoding.ASCII.GetString(request).Trim()}' within {timeout.TotalMilliseconds:0} ms ({received.WrittenCount} bytes received).");
                var n = await _transport.ReadAsync(chunk, remaining < ReadSlice ? remaining : ReadSlice, ct);
                if (n > 0)
                {
                    received.Write(chunk.AsSpan(0, n));
                    if (isComplete(received.WrittenSpan)) return received.WrittenSpan.ToArray();
                }
                else
                {
                    await Task.Delay(15, ct);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Discards late replies (e.g. from a key that timed out) so they cannot be mistaken for the next answer.</summary>
    private async Task DrainAsync(CancellationToken ct)
    {
        var chunk = new byte[1024];
        while (await _transport.ReadAsync(chunk, TimeSpan.FromMilliseconds(5), ct) > 0) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
        _gate.Dispose();
    }
}
