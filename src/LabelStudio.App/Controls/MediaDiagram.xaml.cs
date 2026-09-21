using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LabelStudio.App.Controls;

/// <summary>Schematic media/sensor diagrams for the compatibility checker and media setup (spec §6).</summary>
public sealed partial class MediaDiagram : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(MediaDiagram), new PropertyMetadata("", (d, _) => ((MediaDiagram)d).Show()));

    public MediaDiagram() => InitializeComponent();

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Show()
    {
        foreach (var layer in new FrameworkElement[] { GapCentre, MarkLeft, MinimumSize, TearBar })
            layer.Visibility = layer.Name == Kind ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, new ResourceLoader().GetString($"MediaDiagram_{Kind}"));
    }
}
