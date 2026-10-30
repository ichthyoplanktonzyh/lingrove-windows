using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.ExpressionAssist;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Converts completed writing cards into detached save intents, adapting Mac VocabularyCaptureFactory
/// and VocabularyPolicy.example. Sentence/word facts come from the existing platform adapters;
/// UUID/time belong to a newly encountered usage. No focus, model, notebook or IO lives here.
/// Reading, selection and OCR boundaries require their own acquisition values before being exposed.
/// </summary>
public sealed class VocabularyCaptureFactory(ILearningLexicalText lexicalText, ILearningEvidenceSentenceTokenizer sentenceTokenizer,
    TimeProvider? clock = null)
{
    private readonly ILearningLexicalText _lexicalText = lexicalText ?? throw new ArgumentNullException(nameof(lexicalText));
    private readonly ILearningEvidenceSentenceTokenizer _sentences = sentenceTokenizer ?? throw new ArgumentNullException(nameof(sentenceTokenizer));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    /// <summary>Mac 3b expressionAssist: canonical base form, generated hypothetical encounter,
    /// exact surface range and AI meaning. This does not claim the host accepted an insertion.</summary>
    public VocabularyCapture? FromExpressionAssist(ExpressionAssistCandidate candidate, ExpressionAssistRequest request,
        string? sourceApplicationId = null)
    {
        ArgumentNullException.ThrowIfNull(candidate); ArgumentNullException.ThrowIfNull(request);
        string surface = FoundationText.TrimWhitespaceAndNewlines(candidate.Text);
        if (surface.Length == 0 || LearningLanguages.FromCode(request.TargetLanguage) is not { } language) return null;
        string form = FoundationText.TrimWhitespaceAndNewlines(candidate.DictionaryForm ?? "");
        string canonical = form.Length == 0 ? surface : form;
        var meaningLanguage = LearningLanguages.FromCode(request.ExplanationLanguage);
        string meaning = FoundationText.TrimWhitespaceAndNewlines(candidate.Hint);
        var kind = WordCount(canonical, language) <= 1 ? VocabularyEntryKind.Word : VocabularyEntryKind.Expression;
        VocabularyExample? example = null;
        if (FoundationText.TrimWhitespaceAndNewlines(request.LeftContext).Length > 0
            || FoundationText.TrimWhitespaceAndNewlines(request.RightContext).Length > 0)
        {
            string full = request.LeftContext + candidate.Text + request.RightContext;
            int offset = candidate.Text.IndexOf(surface, StringComparison.Ordinal);
            if (offset >= 0)
                example = Example(full, [new(request.LeftContext.Length + offset, surface.Length)], language, sourceApplicationId);
            if (example is not null) example = example with { SourceKind = Resources.VocabularySourceKind.Generated,
                MeaningOrigin = VocabularyMeaningOrigin.Ai, Meaning = meaning, MeaningLanguage = meaning.Length == 0 ? null : meaningLanguage };
        }
        return new(canonical, canonical, language, kind, meaning, meaning.Length == 0 ? null : meaningLanguage, example);
    }
    public VocabularyCapture? FromWritingSentencePreview(LearningPreviewState presentation, string? sourceApplicationId = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (!Ready(presentation) || presentation.Source.Length == 0 || string.IsNullOrEmpty(presentation.Result)) return null;
        string text = presentation.Result; var language = presentation.Options!.TargetLanguage; var other = presentation.Options.SourceLanguage;
        var kind = WordCount(text, language) == 1 ? VocabularyEntryKind.Word : VocabularyEntryKind.Sentence;
        var example = kind == VocabularyEntryKind.Sentence ? Example(text, [new(0, text.Length)], language, sourceApplicationId) : null;
        if (example is not null) example = example with { Translation = presentation.Source, TranslationLanguage = other };
        return new(text, text, language, kind, presentation.Source, other, example);
    }
    public VocabularyCapture? FromUnit(LearningUnit unit, LearningPreviewState presentation, string? sourceApplicationId = null)
    {
        ArgumentNullException.ThrowIfNull(unit); ArgumentNullException.ThrowIfNull(presentation);
        if (!Ready(presentation)) return null;
        string text = presentation.Result ?? ""; var language = presentation.Options!.TargetLanguage; var other = presentation.Options.SourceLanguage;
        var boundaries = FoundationTextRanges.Boundaries(text);
        if (!unit.Ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, text, boundaries))) return null;
        var example = Example(text, unit.Ranges.Select(range => new VocabularyTextRange(range.Location, range.Length)).ToArray(), language, sourceApplicationId);
        if (WordCount(text, language) <= 1) example = null;
        if (example is not null)
        {
            if (CanonicalStringComparer.Instance.Equals(example.Text, text)) example = example with { Translation = presentation.Source, TranslationLanguage = other };
            // A whole-sentence translation is never a definition of one word inside it.
            example = example with { Meaning = "", MeaningLanguage = other };
        }
        var kind = unit.Kind switch
        {
            LearningUnitKind.Word => VocabularyEntryKind.Word, LearningUnitKind.Expression => VocabularyEntryKind.Expression,
            LearningUnitKind.Pattern => VocabularyEntryKind.Pattern, _ => (VocabularyEntryKind?)null,
        };
        return kind is null ? null : new(unit.Kind == LearningUnitKind.Pattern ? unit.Query : unit.Surface, unit.Query, language, kind.Value, "", other, example);
    }
    public static VocabularyCapture? FromComponent(LearningDictionaryWord word, LearningUnit unit, VocabularyCapture capture)
    {
        ArgumentNullException.ThrowIfNull(word); ArgumentNullException.ThrowIfNull(unit); ArgumentNullException.ThrowIfNull(capture);
        if (!unit.Words.Contains(word)) return null;
        var example = capture.Example;
        if (unit.Ranges.Count > 0 && example is { Ranges.Count: > 0 })
        {
            long offset = (long)unit.Ranges[0].Location - example.Ranges[0].Location;
            long location = word.Range.Location - offset;
            if (location is < 0 or > int.MaxValue) return null;
            var range = new VocabularyTextRange((int)location, word.Range.Length);
            if (!FoundationTextRanges.IsValid(range.Location, range.Length, example.Text, FoundationTextRanges.Boundaries(example.Text))
                || !CanonicalStringComparer.Instance.Equals(example.Text.Substring(range.Location, range.Length), word.Text)) return null;
            // This is the same encountered sentence, retaining its identity, provenance and
            // translation. Only the exact component occurrence and its contextual meaning change.
            example = example with { Ranges = [range], Meaning = "", MeaningLanguage = null };
        }
        else example = null;
        return new(word.Text, word.Text, capture.Language, VocabularyEntryKind.Word, Example: example)
        { AllowsExplanationEnrichment = capture.AllowsExplanationEnrichment };
    }
    private static bool Ready(LearningPreviewState presentation) => presentation.Status == LearningPreviewStatus.Ready && presentation.Options is not null;
    private int WordCount(string text, LearningLanguage language)
    {
        try { return _lexicalText.Words(text, language)?.Count ?? throw new LearningLexicalException(LearningLexicalFailure.TextProviderUnavailable); }
        catch (LearningLexicalException) { throw; }
        catch { throw new LearningLexicalException(LearningLexicalFailure.TextProviderUnavailable); }
    }
    private VocabularyExample? Example(string text, IReadOnlyList<VocabularyTextRange> ranges, LearningLanguage language, string? app)
    {
        if (text.Length == 0 || !LearningExplanationText.Fits(text, VocabularyCapturePolicy.MaximumTextBytes) || ranges.Count == 0) return null;
        var boundaries = FoundationTextRanges.Boundaries(text);
        if (!ranges.All(range => FoundationTextRanges.IsValid(range.Location, range.Length, text, boundaries))) return null;
        int lower = ranges.Min(range => range.Location), upper = ranges.Max(range => range.Location + range.Length);
        LearningTextRange? sentence = null;
        foreach (var candidate in SentenceRanges(text, language, boundaries))
            if (candidate.Location <= lower && candidate.Location + candidate.Length >= upper) { sentence = candidate; break; }
        var scope = sentence ?? new LearningTextRange(0, text.Length);
        return new(Guid.NewGuid(), text.Substring(scope.Location, scope.Length), "",
            ranges.Select(range => new VocabularyTextRange(range.Location - scope.Location, range.Length)).ToArray(),
            sentence is null ? VocabularyExampleCompleteness.Fragment : VocabularyExampleCompleteness.Sentence, "", _clock.GetUtcNow()) { SourceApplicationId = app };
    }
    private IReadOnlyList<LearningTextRange> SentenceRanges(string text, LearningLanguage language, FoundationComposedText boundaries)
    {
        try
        {
            var source = _sentences.Tokenize(text, language) ?? throw SentenceUnavailable(); var result = new List<LearningTextRange>(); int end = 0;
            // Freeze and validate the complete provider response. A corrupt later token cannot
            // make an earlier matching sentence look trustworthy merely because we stopped early.
            foreach (var candidate in source)
            {
                if (candidate.Location < end || !FoundationTextRanges.IsValid(candidate.Location, candidate.Length, text, boundaries)) throw SentenceUnavailable();
                end = candidate.Location + candidate.Length; result.Add(candidate);
            }
            return result;
        }
        catch (LearningTranslationException) { throw; }
        catch { throw SentenceUnavailable(); }
    }
    private static LearningTranslationException SentenceUnavailable() => new(LearningTranslationFailure.SentenceTokenizerUnavailable);
}
