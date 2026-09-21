using LabelStudio.ViewModels.Printers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views.Printers;

public sealed partial class SmartCalPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(CalibrationViewModel), typeof(SmartCalPanel),
        new PropertyMetadata(null, (d, _) => ((SmartCalPanel)d).Bindings.Update()));

    public SmartCalPanel() => InitializeComponent();

    public CalibrationViewModel? ViewModel
    {
        get => (CalibrationViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
