namespace LabelStudio.Devices.Transport;

public interface IPrinterTransport : IAsyncDisposable
{
    Task OpenAsync(CancellationToken ct);
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    /// <summary>Reads what is available. Returns 0 if nothing arrived within <paramref name="timeout"/>.</summary>
    Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct);
}
