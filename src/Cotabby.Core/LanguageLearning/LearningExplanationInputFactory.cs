using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Projects the Mac capture-factory/input composition onto the current completed writing preview.
/// It owns no editor or notebook: a future card supplies its immutable document/unit, and this
/// adapter retains original UTF-16 occurrences for explanation. Platform word/sentence facts are
/// injected; callers run them on a worker and separately verify their presentation identity.
/// Reading/original-text/selection capture and notebook persistence remain separate work.
/// </summary>
public sealed class LearningExplanationInputFactory(ILearningLexicalText lexicalText, ILearningEvidenceSentenceTokenizer sentenceTokenizer)
{
    private readonly VocabularyCaptureFactory _captures = new(lexicalText, sentenceTokenizer);

    /// <summary>Explanation projects the same detached writing capture used by durable Save.</summary>
    public LearningExplanationInput? FromWritingSentencePreview(LearningPreviewState presentation) =>
        _captures.FromWritingSentencePreview(presentation) is { } capture ? LearningExplanationPolicy.Input(capture) : null;

    public LearningExplanationInput? FromUnit(LearningUnit unit, LearningPreviewState presentation) =>
        _captures.FromUnit(unit, presentation) is { } capture ? LearningExplanationPolicy.Input(capture) : null;

    public LearningExplanationInput? FromComponent(LearningDictionaryWord word, LearningUnit unit, LearningExplanationInput capture)
        => ProjectComponent(word, unit, capture);

    /// <summary>
    /// Component projection needs no tokenizer or editor. A dictionary target uses this same
    /// occurrence mapping when navigating its frozen unit, without carrying a platform adapter.
    /// </summary>
    public static LearningExplanationInput? ProjectComponent(LearningDictionaryWord word, LearningUnit unit, LearningExplanationInput capture)
    {
        ArgumentNullException.ThrowIfNull(word); ArgumentNullException.ThrowIfNull(unit); ArgumentNullException.ThrowIfNull(capture);
        if (!unit.Words.Contains(word)) return null;
        Evidence? evidence = null;
        if (capture.ContextKind != LearningExplanationContext.None && unit.Ranges.Count > 0 && capture.Ranges.Count > 0)
        {
            // The sentence window may start after offset zero. Map the clicked occurrence through
            // the original/local first-range offset; searching repeated word text would lose it.
            long offset = (long)unit.Ranges[0].Location - capture.Ranges[0].Location;
            long location = word.Range.Location - offset;
            if (location is < 0 or > int.MaxValue) return null;
            var range = new VocabularyTextRange((int)location, word.Range.Length);
            if (!LearningExplanationText.ValidRange(range.Location, range.Length, capture.Context,
                LearningExplanationText.Boundaries(capture.Context))) return null;
            if (!CanonicalStringComparer.Instance.Equals(capture.Context.Substring(range.Location, range.Length), word.Text)) return null;
            evidence = new(capture.Context, [range], capture.ContextKind);
        }
        return Input(word.Text, word.Text, capture.Language, VocabularyEntryKind.Word, evidence);
    }

    /// <summary>
    /// Mac's unit fallback when an example could not be captured. General explanation stays
    /// available, but this value makes no claim to contain the original sentence or occurrence.
    /// </summary>
    public static LearningExplanationInput? FromUnscopedUnit(LearningUnit unit, LearningLanguage language)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var kind = unit.Kind switch
        {
            LearningUnitKind.Word => VocabularyEntryKind.Word,
            LearningUnitKind.Expression => VocabularyEntryKind.Expression,
            LearningUnitKind.Pattern => VocabularyEntryKind.Pattern,
            _ => (VocabularyEntryKind?)null,
        };
        return kind is null ? null : Input(unit.Kind == LearningUnitKind.Pattern ? unit.Query : unit.Surface,
            unit.Query, language, kind.Value, null);
    }

    private static LearningExplanationInput Input(string target, string form, LearningLanguage language,
        VocabularyEntryKind kind, Evidence? evidence) => new(target, form, language, kind, evidence?.Text ?? target,
            evidence?.Ranges ?? [new VocabularyTextRange(0, target.Length)], evidence?.Scope ?? LearningExplanationContext.None,
            evidence is null ? null : Receipt(evidence));

    // The same exact usage receipt now belongs to the vocabulary identity boundary. Reuse it
    // without constructing a durable usage, UUID or timestamp for a transient explanation request.
    private static string Receipt(Evidence evidence) => VocabularyCapturePolicy.ExampleKey(evidence.Text,
        evidence.Ranges, evidence.Scope == LearningExplanationContext.Sentence
            ? VocabularyExampleCompleteness.Sentence : VocabularyExampleCompleteness.Fragment);

    /// <summary>Transient projection of a captured example, owned only while constructing the frozen input.</summary>
    private sealed record Evidence(string Text, IReadOnlyList<VocabularyTextRange> Ranges, LearningExplanationContext Scope);
}
