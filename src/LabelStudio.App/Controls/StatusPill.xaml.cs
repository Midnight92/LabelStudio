using System.Windows.Input;
using LabelStudio.ViewModels.Status;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabelStudio.App.Controls;

public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(StatusTone), typeof(StatusPill), new PropertyMetadata(StatusTone.Neutral, (d, _) => ((StatusPill)d).UpdateTone()));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatusPill), new PropertyMetadata(""));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(""));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(StatusPill), new PropertyMetadata(null));

    public StatusPill()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateTone();
    }

    public StatusTone Tone { get => (StatusTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private void UpdateTone() => VisualStateManager.GoToState(this, Tone.ToString(), useTransitions: false);
}
