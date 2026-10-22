namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Card-owned geometry adapted from the Mac value: first completed content locks a compact
/// 120–300 point viewport for that query; loading/errors may measure without locking. This
/// state contains no WPF objects, so provider changes cannot silently resize a resolved card.
/// </summary>
public sealed class LearningDictionaryViewport
{
    private string? _query;
    private double _height = 120;
    private bool _locked;
    public double HeightFor(string query) => LearningLexicalCollectionValue.StringEquals(_query, query) ? _height : 120;
    public void Measure(double naturalHeight, string query, bool resolved)
    {
        if (!double.IsFinite(naturalHeight) || naturalHeight <= 0) return;
        if (!LearningLexicalCollectionValue.StringEquals(_query, query)) { _query = query; _locked = false; }
        if (_locked) return;
        _height = Math.Clamp(Math.Ceiling(naturalHeight), 120, 300);
        _locked = resolved;
    }
}
