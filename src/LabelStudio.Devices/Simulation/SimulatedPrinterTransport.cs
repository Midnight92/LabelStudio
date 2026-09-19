using System.Text;
using LabelStudio.Devices.Transport;

namespace LabelStudio.Devices.Simulation;

public sealed class SimulatedPrinterTransport(SimulatedPrinter printer) : IPrinterTransport
{
    private readonly Queue<byte> _pending = new();
    private bool _open;

    public Task OpenAsync(CancellationToken ct)
    {
        if (printer.Unplugged) throw new PrinterUnavailableException("Simulated printer is unplugged.", PrinterUnavailableReason.NotFound);
        if (printer.Claimed) throw new PrinterUnavailableException("Simulated printer is held by another application.", PrinterUnavailableReason.Claimed);
        _open = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        EnsureUsable();
        foreach (var b in Encoding.Latin1.GetBytes(printer.Respond(Encoding.UTF8.GetString(data.Span)))) _pending.Enqueue(b);
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct)
    {
        EnsureUsable();
        var span = buffer.Span;
        var n = 0;
        while (n < span.Length && _pending.Count > 0) span[n++] = _pending.Dequeue();
        return Task.FromResult(n);
    }

    public ValueTask DisposeAsync()
    {
        _open = false;
        return ValueTask.CompletedTask;
    }

    private void EnsureUsable()
    {
        if (!_open) throw new InvalidOperationException("Transport is not open.");
        if (printer.Unplugged) throw new IOException("Simulated printer was unplugged.");
    }
}
