using LabelStudio.ViewModels.Printers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views.Printers;

public sealed partial class MediaSetupPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MediaSetupViewModel), typeof(MediaSetupPanel),
        new PropertyMetadata(null, (d, _) => ((MediaSetupPanel)d).Bindings.Update()));

    public MediaSetupPanel() => InitializeComponent();

    public MediaSetupViewModel? ViewModel
    {
        get => (MediaSetupViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Deep-link target for <see cref="LabelStudio.ViewModels.PrinterSection.LengthOnly"/>.</summary>
    public FrameworkElement LengthOnlyTarget => LengthOnlySection;
}
