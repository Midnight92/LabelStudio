using LabelStudio.ViewModels.Home;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Views;

public sealed partial class FirstRunView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(FirstRunViewModel), typeof(FirstRunView),
        new PropertyMetadata(null, (d, _) => ((FirstRunView)d).Bindings.Update()));

    public FirstRunView() => InitializeComponent();

    public FirstRunViewModel? ViewModel
    {
        get => (FirstRunViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
