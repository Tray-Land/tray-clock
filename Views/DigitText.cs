using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TrayClock.Views;

/// <summary>
/// Text drawn one character per fixed-width slot, so a ticking readout stays put. Segoe UI
/// Variable's digits aren't all the same width and WinUI has no tabular-figures switch, so plain
/// text re-centers (and visibly jiggles) every time a "1" comes or goes. Font, size, weight, and
/// color come from the control's own properties, which the slots inherit.
/// </summary>
public sealed partial class DigitText : UserControl
{
    // Slot widths as a fraction of the font size: wide enough for Segoe UI Variable's widest digit.
    private const double DigitWidth = 0.58;
    private const double SeparatorWidth = 0.3;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DigitText), new PropertyMetadata("", (d, _) => ((DigitText)d).Rebuild()));

    private readonly StackPanel _slots = new() { Orientation = Orientation.Horizontal };

    public DigitText()
    {
        Content = _slots;
        IsTabStop = false;
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Rebuild());
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Rebuild()
    {
        string text = Text ?? "";

        // Reuse the slots: the stopwatch updates this 20 times a second.
        while (_slots.Children.Count > text.Length)
        {
            _slots.Children.RemoveAt(_slots.Children.Count - 1);
        }

        while (_slots.Children.Count < text.Length)
        {
            _slots.Children.Add(new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                TextLineBounds = TextLineBounds.TrimToBaseline,
            });
        }

        for (int i = 0; i < text.Length; i++)
        {
            var slot = (TextBlock)_slots.Children[i];
            slot.Text = text[i].ToString();
            slot.Width = FontSize * (char.IsDigit(text[i]) ? DigitWidth : SeparatorWidth);
        }
    }
}
