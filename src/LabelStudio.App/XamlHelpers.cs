using LabelStudio.ViewModels.Status;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App;

public static class XamlHelpers
{
    public static InfoBarSeverity ToSeverity(StatusTone tone) => tone switch
    {
        StatusTone.Success => InfoBarSeverity.Success,
        StatusTone.Caution => InfoBarSeverity.Warning,
        StatusTone.Critical => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    public static Visibility VisibleWhenNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static bool IsNotNull(object? value) => value is not null;

    /// <summary>Status colour for a list-row dot. Always paired with a glyph and text (never colour alone).</summary>
    public static Microsoft.UI.Xaml.Media.Brush ToneBrush(StatusTone tone) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[tone switch
        {
            StatusTone.Success => "SystemFillColorSuccessBrush",
            StatusTone.Caution => "SystemFillColorCautionBrush",
            StatusTone.Critical => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush",
        }];

    public static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static bool Not(bool value) => !value;

    public static InfoBarSeverity VerdictSeverity(bool compatible) => compatible ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    public static bool ConnectedButReadOnly(bool connected, bool canEdit) => connected && !canEdit;
}
