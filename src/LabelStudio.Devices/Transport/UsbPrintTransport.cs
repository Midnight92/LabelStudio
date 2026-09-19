using System.ComponentModel;
using System.Runtime.InteropServices;
using static LabelStudio.Devices.Transport.NativeMethods;

namespace LabelStudio.Devices.Transport;

/// <summary>Bidirectional raw channel over the usbprint device interface. Works alongside an installed ZDesigner driver.</summary>
public sealed class UsbPrintTransport(string devicePath) : IPrinterTransport
{
    private FileStream? _stream;

    public Task OpenAsync(CancellationToken ct)
    {
        var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            var reason = error switch
            {
                ErrorAccessDenied or ErrorSharingViolation => PrinterUnavailableReason.Claimed,
                ErrorFileNotFound or ErrorPathNotFound => PrinterUnavailableReason.NotFound,
                _ => PrinterUnavailableReason.IoFailure,
            };
            throw new PrinterUnavailableException($"Could not open {devicePath} (Win32 error {error}).", reason, new Win32Exception(error));
        }
        _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await Stream.WriteAsync(data, ct);
        await Stream.FlushAsync(ct);
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await Stream.ReadAsync(buffer, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return 0; // read timed out with nothing pending
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _stream = null;
    }

    private FileStream Stream => _stream ?? throw new InvalidOperationException("Transport is not open.");
}
