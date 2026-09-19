// Isolated spike for Task 6 (M1 device foundation): tries Zebra.Printer.SDK 5.0.3685
// against the real ZD220t printer, read-only (discovery, GetCurrentStatus, SGD.GET only).
// Not part of LabelStudio.slnx and never referenced by LabelStudio.App or LabelStudio.Devices
// -- see docs/decisions/0001-printer-transport.md.
using Zebra.Sdk.Comm;
using Zebra.Sdk.Printer;
using Zebra.Sdk.Printer.Discovery;

// ctx7 (techdocs.zebra.com/link-os/3-00) confirms GetZebraUsbPrinters() is parameterless in the
// .NET SDK and returns List<DiscoveredUsbPrinter>; the brief's ZebraPrinterFilter overload does
// not exist for USB discovery, so it was dropped here.
var usb = UsbDiscoverer.GetZebraUsbPrinters().FirstOrDefault();
if (usb is null)
{
    Console.Error.WriteLine("SDK found no Zebra USB printer.");
    return 1;
}

Console.WriteLine($"SDK discovered: {usb}");
var connection = usb.GetConnection();
connection.Open();
try
{
    // ZebraPrinter tier only -- never ZebraPrinterLinkOs on the ZD220 (Link-OS Basic).
    var printer = ZebraPrinterFactory.GetInstance(connection);
    var status = printer.GetCurrentStatus();
    Console.WriteLine(
        $"ready={status.isReadyToPrint} paused={status.isPaused} headOpen={status.isHeadOpen} " +
        $"paperOut={status.isPaperOut} ribbonOut={status.isRibbonOut}");
    Console.WriteLine($"appl.name={SGD.GET("appl.name", connection)}");
}
finally
{
    connection.Close();
}

return 0;
