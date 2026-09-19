namespace LabelStudio.Devices.Capabilities;

public static class SgdKeys
{
    public const string ApplName = "appl.name";
    public const string Languages = "device.languages";
    public const string UniqueId = "device.unique_id";
    public const string FriendlyName = "device.friendly_name";
    public const string ProductName = "device.product_name";
    public const string ResolutionDpi = "head.resolution.in_dpi";
    public const string MediaType = "ezpl.media_type";
    public const string PrintMethod = "ezpl.print_method";
    public const string PrintMode = "media.printmode";
    public const string LabelLength = "zpl.label_length";
    public const string PrintWidth = "ezpl.print_width";
    public const string Darkness = "print.tone";
    public const string PrintSpeed = "media.speed";
    public const string TearOff = "ezpl.tear_off";
    public const string SenseMode = "media.sense_mode";
    public const string HeadCloseAction = "ezpl.head_close_action";
    public const string PowerUpAction = "ezpl.power_up_action";
    public const string OdometerTotal = "odometer.total_print_length";
    public const string OdometerHeadClean = "odometer.headclean";
    public const string OdometerUserLabels = "odometer.user_label_count";

    public static readonly IReadOnlyList<string> ProbeList =
    [
        ApplName, Languages, UniqueId, FriendlyName, ProductName, ResolutionDpi,
        MediaType, PrintMethod, PrintMode, LabelLength, PrintWidth, Darkness, PrintSpeed, TearOff,
        SenseMode, HeadCloseAction, PowerUpAction, OdometerTotal, OdometerHeadClean, OdometerUserLabels,
    ];

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
}
