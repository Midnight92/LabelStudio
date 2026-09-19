using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Status;

public enum PrinterState { Ready, Paused, HeadOpen, MediaOut, RibbonOut, OverTemperature, UnderTemperature }

public static class PrinterStateResolver
{
    /// <summary>Faults outrank Paused because the printer pauses itself when it faults.</summary>
    public static PrinterState Resolve(HostStatus s) => s switch
    {
        { HeadUp: true } => PrinterState.HeadOpen,
        { PaperOut: true } => PrinterState.MediaOut,
        { RibbonOut: true, ThermalTransferMode: true } => PrinterState.RibbonOut,
        { OverTemperature: true } => PrinterState.OverTemperature,
        { UnderTemperature: true } => PrinterState.UnderTemperature,
        { Paused: true } => PrinterState.Paused,
        _ => PrinterState.Ready,
    };
}
