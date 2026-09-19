using LabelStudio.Devices.Discovery;

namespace LabelStudio.Devices.Tests.Discovery;

public class UsbDevicePathTests
{
    [Fact]
    public void Parses_usbprint_interface_path()
    {
        Assert.True(UsbDevicePath.TryParse(@"\\?\USB#VID_0A5F&PID_0164#ABC123456789#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}", out var vid, out var pid, out var serial));
        Assert.Equal(0x0A5F, vid);
        Assert.Equal(0x0164, pid);
        Assert.Equal("ABC123456789", serial);
    }

    [Fact]
    public void Rejects_non_usb_paths() => Assert.False(UsbDevicePath.TryParse(@"\\?\SWD#PRINTENUM#{87B20435}", out _, out _, out _));
}
