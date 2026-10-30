using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.Localization;
using Cotabby.Core.Settings;

namespace Cotabby.App.UI;

/// <summary>Fixed first-use illustration only. No focus, capture, model, editor or clipboard is
/// involved. The timer belongs to this visible control and stops when its tab/page is hidden.</summary>
internal sealed class WelcomeCompletionDemo : Border
{
    private static readonly string[] Examples = ["Thanks for the¦ quick reply!", "Let's meet¦ tomorrow at 10.", "Here's the¦ final draft."];
    private readonly TextBlock _line = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly DispatcherTimer _timer = new();
    private int _example, _typed, _phase;
    private string[] _characters = [];
    private string _ghost = "";
    internal bool IsAnimating => _timer.IsEnabled;
    internal WelcomeCompletionDemo()
    {
        CornerRadius = new CornerRadius(8); Padding = new Thickness(12); MinHeight = 64; Child = _line;
        SetResourceReference(BackgroundProperty, "InputBg");
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _timer.Stop(); return; }
            Reset();
            if (SystemParameters.ClientAreaAnimation) { _timer.Interval = TimeSpan.FromMilliseconds(450); _timer.Start(); }
            else { _typed = _characters.Length; _phase = 3; Draw(); }
        };
    }
    private void Reset()
    {
        string example = InterfaceText.Get(Examples[_example % Examples.Length]);
        var sentence = new CompletionPreviewText(example); _ghost = sentence.GhostText;
        var enumerator = StringInfo.GetTextElementEnumerator(sentence.TypedText); var items = new List<string>();
        while (enumerator.MoveNext()) items.Add(enumerator.GetTextElement());
        _characters = items.ToArray(); _typed = 0; _phase = 0; Draw();
    }
    private void Advance()
    {
        switch (_phase)
        {
            case 0:
                _typed++;
                if (_typed >= _characters.Length) { _phase = 1; _timer.Interval = TimeSpan.FromMilliseconds(280); }
                else _timer.Interval = TimeSpan.FromMilliseconds(52);
                break;
            case 1: _phase = 2; _timer.Interval = TimeSpan.FromMilliseconds(1500); break;
            case 2: _phase = 3; _timer.Interval = TimeSpan.FromMilliseconds(1100); break;
            default: _example++; Reset(); _timer.Interval = TimeSpan.FromMilliseconds(500); break;
        }
        Draw();
    }
    private void Draw()
    {
        _line.Inlines.Clear(); _line.Inlines.Add(new Run(string.Concat(_characters.Take(_typed))));
        if (_phase < 2) return;
        var suffix = new Run(_ghost);
        if (_phase == 2) suffix.SetResourceReference(TextElement.ForegroundProperty, "TextSecondary");
        _line.Inlines.Add(suffix);
        if (_phase == 2) _line.Inlines.Add(new Run("  Tab"));
    }
}
