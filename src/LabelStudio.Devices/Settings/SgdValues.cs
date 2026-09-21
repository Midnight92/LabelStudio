namespace LabelStudio.Devices.Settings;

/// <summary>Canonical SGD value strings (Zebra SGD reference; confirmed on hardware in M2a Task 1).</summary>
public static class SgdValues
{
    public static class MediaType
    {
        public const string Continuous = "continuous";
        public const string GapNotch = "gap/notch";
        public const string Mark = "mark";
    }

    public static class PrintMode
    {
        public const string TearOff = "tear off";
        public const string Peel = "peel off";
    }

    public static class PrintMethod
    {
        public const string ThermalTransfer = "thermal trans";
        public const string DirectThermal = "direct thermal";
    }
}
