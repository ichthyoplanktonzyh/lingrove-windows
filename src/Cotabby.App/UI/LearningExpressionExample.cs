using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.App.UI.Controls;
using Cotabby.Core.ExpressionAssist;

namespace Cotabby.App.UI;

/// <summary>Bounded, in-memory expression example. It embeds the production candidate view but
/// never creates an editor, runtime, clipboard, service, or user-data owner.</summary>
internal sealed class LearningExpressionExample : StackPanel
{
    private static readonly ExpressionAssistResult Sample = new([
        new("keep me posted", "让我随时了解最新情况", "keep someone posted"),
        new("keep me updated", "持续向我提供最新消息", null)], null);
    private readonly TextBlock _line = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ExpressionAssistView _candidate = new() { Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Func<string> _trigger;
    private string _selected = "keep me posted";
    private int _step;
    private bool _updating;
    internal event Action? Interacted;
    internal event Action? Inserted;
    internal int Step => _step;
    internal string ExampleText => _line.Text;

    internal LearningExpressionExample(Func<string>? trigger = null)
    {
        _trigger = trigger ?? (() => "??");
        _candidate.QueryBox.IsReadOnly = true;
        Children.Add(new Border { Child = _line, CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) }
            .Themed(Border.BackgroundProperty, "InputBg"));
        Children.Add(_candidate);
        _candidate.Accept += candidate => { _selected = candidate.Text; Interacted?.Invoke(); SetStep(3); Inserted?.Invoke(); };
        _candidate.Cancel += () => { Interacted?.Invoke(); SetStep(0); };
        _candidate.QueryChanged += (_, _) => { if (!_updating) Interacted?.Invoke(); };
        SetStep(0);
    }

    internal void SetStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        if (_step == 0) _selected = "keep me posted";
        string symbols = _trigger();
        _line.Text = _step switch
        {
            0 => $"Please {symbols} on the project's progress.",
            1 or 2 => $"Please {symbols}让我随时了解最新进展 on the project's progress.",
            _ => $"Please {_selected} on the project's progress.",
        };
        _candidate.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_step == 2)
        {
            _updating = true;
            try { _candidate.SetQuery("让我随时了解最新进展"); _candidate.Update(Sample, null, false); }
            finally { _updating = false; }
        }
    }
}
