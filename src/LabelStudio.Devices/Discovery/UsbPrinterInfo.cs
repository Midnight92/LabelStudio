namespace LabelStudio.Devices.Discovery;

public sealed record UsbPrinterInfo(string DevicePath, ushort VendorId, ushort ProductId, string Serial, string FriendlyName);
