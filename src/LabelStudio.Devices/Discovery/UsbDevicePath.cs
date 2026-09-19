using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelStudio.Devices.Discovery;

public static partial class UsbDevicePath
{
    public const ushort ZebraVendorId = 0x0A5F;

    public static bool TryParse(string devicePath, out ushort vendorId, out ushort productId, out string serial)
    {
        var m = Pattern().Match(devicePath);
        if (!m.Success)
        {
            (vendorId, productId, serial) = (0, 0, "");
            return false;
        }
        vendorId = ushort.Parse(m.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        productId = ushort.Parse(m.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        serial = m.Groups["serial"].Value.ToUpperInvariant();
        return true;
    }

    [GeneratedRegex(@"usb#vid_(?<vid>[0-9a-f]{4})&pid_(?<pid>[0-9a-f]{4})#(?<serial>[^#]+)#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
