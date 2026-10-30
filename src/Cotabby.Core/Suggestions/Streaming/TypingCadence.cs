using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Suggestions.Streaming;

/// <summary>Short-lived timing only; retains no typed characters.</summary>
public sealed class TypingCadence
{
    private long? _identity;
    private double? _lastLetter;
    private readonly Queue<double> _intervals = new();
    public void Record(long identity, string characters, double time)
    {
        if (!double.IsFinite(time)) return;
        if (_identity != identity) { _identity = identity; _lastLetter = null; _intervals.Clear(); }
        var graphemes = new GraphemeString(characters);
        if (graphemes.Count != 1 || !graphemes[0].IsLetter) { _lastLetter = null; return; }
        if (_lastLetter is { } last)
        {
            double interval = time - last;
            if (interval is >= .025 and <= .6) { _intervals.Enqueue(interval); while (_intervals.Count > 6) _intervals.Dequeue(); }
            else _intervals.Clear();
        }
        _lastLetter = time;
    }
    public double RemainingDelay(long identity, string precedingText, double time)
    {
        if (_identity != identity || _lastLetter is not { } last || CaretWordContext.UnfinishedWord(precedingText) is null) return 0;
        var sorted = _intervals.Order().ToArray();
        double quiet = sorted.Length >= 2 ? Math.Clamp(sorted[sorted.Length / 2] * 1.2, .08, .22) : .08;
        return Math.Max(0, quiet - Math.Max(0, time - last));
    }
}
