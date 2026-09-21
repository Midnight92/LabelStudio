using LabelStudio.ViewModels.Printers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views.Printers;

public sealed partial class CompatibilityCheckerPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(CompatibilityCheckerViewModel), typeof(CompatibilityCheckerPanel),
        new PropertyMetadata(null, (d, _) => ((CompatibilityCheckerPanel)d).Bindings.Update()));

    public CompatibilityCheckerPanel() => InitializeComponent();

    public CompatibilityCheckerViewModel? ViewModel
    {
        get => (CompatibilityCheckerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
