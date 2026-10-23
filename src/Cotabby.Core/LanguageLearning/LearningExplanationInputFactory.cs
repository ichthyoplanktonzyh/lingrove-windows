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
    private readonly ILearningLexicalText _lexicalText = lexicalText ?? throw new ArgumentNullException(nameof(lexicalText));
    private readonly ILearningEvidenceSentenceTokenizer _sentences = sentenceTokenizer ?? throw new ArgumentNullException(nameof(sentenceTokenizer));

    /// <summary>
    /// Captures only the current manual caret-sentence writing preview. A future selection or
    /// reading flow must carry its Mac capture origin and use its corresponding capture adapter.
    /// </summary>
    public LearningExplanationInput? FromWritingSentencePreview(LearningPreviewState presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (!Ready(presentation) || presentation.Source.Length == 0 || string.IsNullOrEmpty(presentation.Result)) return null;
        string text = presentation.Result;
        var language = presentation.Options!.TargetLanguage;
        var kind = WordCount(text, language) == 1 ? VocabularyEntryKind.Word : VocabularyEntryKind.Sentence;
        var evidence = kind == VocabularyEntryKind.Sentence ? Example(text, [new(0, text.Length)], language) : null;
        return Input(text, text, language, kind, evidence);
    }

    public LearningExplanationInput? FromUnit(LearningUnit unit, LearningPreviewState presentation)
    {
        ArgumentNullException.ThrowIfNull(unit); ArgumentNullException.ThrowIfNull(presentation);
        if (!Ready(presentation)) return null;
        string text = presentation.Result ?? string.Empty;
        var language = presentation.Options!.TargetLanguage;
        var boundaries = LearningExplanationText.Boundaries(text);
        if (!unit.Ranges.All(range => LearningExplanationText.ValidRange(range.Location, range.Length, text, boundaries))) return null;
        var ranges = unit.Ranges.Select(range => new VocabularyTextRange(range.Location, range.Length)).ToArray();
        var evidence = Example(text, ranges, language);
        if (WordCount(text, language) <= 1) evidence = null;
        VocabularyEntryKind? kind = unit.Kind switch
        {
            LearningUnitKind.Word => VocabularyEntryKind.Word,
            LearningUnitKind.Expression => VocabularyEntryKind.Expression,
            LearningUnitKind.Pattern => VocabularyEntryKind.Pattern,
            _ => null,
        };
        if (kind is null) return null;
        return Input(unit.Kind == LearningUnitKind.Pattern ? unit.Query : unit.Surface,
            unit.Query, language, kind.Value, evidence);
    }

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

    private static bool Ready(LearningPreviewState presentation) =>
        presentation.Status == LearningPreviewStatus.Ready && presentation.Options is not null;

    /// <summary>
    /// This is only the text/range/completeness projection of VocabularyPolicy.example. UUIDs,
    /// meanings, translated examples and app provenance never enter ExplanationInput on Mac.
    /// </summary>
    private Evidence? Example(string text, IReadOnlyList<VocabularyTextRange> ranges, LearningLanguage language)
    {
        if (text.Length == 0 || !LearningExplanationText.Fits(text, ModelContextBudget.MaximumTextBytes) || ranges.Count == 0) return null;
        var boundaries = LearningExplanationText.Boundaries(text);
        if (!ranges.All(range => LearningExplanationText.ValidRange(range.Location, range.Length, text, boundaries))) return null;
        int lower = ranges.Min(range => range.Location), upper = ranges.Max(range => range.Location + range.Length);
        LearningTextRange? sentence = null;
        foreach (var candidate in SentenceRanges(text, language, boundaries))
        {
            if (candidate.Location <= lower && (long)candidate.Location + candidate.Length >= upper) { sentence = candidate; break; }
        }
        var scope = sentence ?? new LearningTextRange(0, text.Length);
        return new(text.Substring(scope.Location, scope.Length),
            ranges.Select(range => new VocabularyTextRange(range.Location - scope.Location, range.Length)).ToArray(),
            sentence is null ? LearningExplanationContext.Fragment : LearningExplanationContext.Sentence);
    }

    private int WordCount(string text, LearningLanguage language)
    {
        try
        {
            return _lexicalText.Words(text, language)?.Count
                ?? throw new LearningLexicalException(LearningLexicalFailure.TextProviderUnavailable);
        }
        catch (LearningLexicalException) { throw; }
        catch (Exception) { throw new LearningLexicalException(LearningLexicalFailure.TextProviderUnavailable); }
    }

    private IReadOnlyList<LearningTextRange> SentenceRanges(string text, LearningLanguage language, int[] boundaries)
    {
        try
        {
            var source = _sentences.Tokenize(text, language);
            if (source is null) throw SentenceUnavailable();
            var result = new List<LearningTextRange>();
            int end = 0;
            // Validate the complete provider response before trusting a containing sentence. A
            // later overlapping/corrupt token invalidates the adapter, even if an earlier one fit.
            // Copying also keeps a mutable or lazy OS result out of the frozen capture operation.
            foreach (var candidate in source)
            {
                if (candidate.Location < end || !LearningExplanationText.ValidRange(candidate.Location, candidate.Length, text, boundaries))
                    throw SentenceUnavailable();
                end = candidate.Location + candidate.Length;
                result.Add(candidate);
            }
            return result;
        }
        catch (LearningTranslationException) { throw; }
        catch (Exception) { throw SentenceUnavailable(); }
    }

    private static LearningTranslationException SentenceUnavailable() =>
        new(LearningTranslationFailure.SentenceTokenizerUnavailable);

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
