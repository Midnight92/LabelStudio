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
}
