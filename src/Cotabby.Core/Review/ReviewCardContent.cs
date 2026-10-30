using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Detached exported material from one saved sense/plan. Preview and future Anki send
/// borrow this value; equality measures content, never dictionary browsing or service state.
/// Optional resolved sides distinguish a flexible card from the legacy recognition/gap layout.</summary>
public sealed record ReviewCardContent
{
    private IReadOnlyList<VocabularyContentBlock> _senseContent = CollectionValues.List<VocabularyContentBlock>([]);
    private IReadOnlyList<ReviewResolvedPart>? _front, _back;
    public ReviewCardContent(string expression, string answer, string sentence, IReadOnlyList<VocabularyTextRange> ranges,
        string translation, string explanation, string notes, string gapPrompt, string gapAnswer, bool recognition, bool gap)
    {
        Expression = expression; Answer = answer; Sentence = sentence; Ranges = CollectionValues.List(ranges);
        Translation = translation; Explanation = explanation; Notes = notes; GapPrompt = gapPrompt; GapAnswer = gapAnswer;
        Recognition = recognition; Gap = gap;
    }
    public string Expression { get; }
    public string Answer { get; }
    public string Sentence { get; }
    public IReadOnlyList<VocabularyTextRange> Ranges { get; }
    public string Translation { get; }
    public string Explanation { get; }
    public string Notes { get; }
    public string GapPrompt { get; }
    public string GapAnswer { get; }
    public bool Recognition { get; }
    public bool Gap { get; }
    public IReadOnlyList<VocabularyContentBlock> SenseContent { get => _senseContent; init => _senseContent = CollectionValues.List(value); }
    public IReadOnlyList<ReviewResolvedPart>? Front { get => _front; init => _front = CollectionValues.OptionalList(value); }
    public IReadOnlyList<ReviewResolvedPart>? Back { get => _back; init => _back = CollectionValues.OptionalList(value); }
    public bool Equals(ReviewCardContent? other) => other is not null
        && CollectionValues.Same(Expression, other.Expression) && CollectionValues.Same(Answer, other.Answer)
        && CollectionValues.Same(Sentence, other.Sentence) && CollectionValues.SequenceEqual(Ranges, other.Ranges)
        && CollectionValues.Same(Translation, other.Translation) && CollectionValues.Same(Explanation, other.Explanation)
        && CollectionValues.Same(Notes, other.Notes) && CollectionValues.Same(GapPrompt, other.GapPrompt)
        && CollectionValues.Same(GapAnswer, other.GapAnswer) && Recognition == other.Recognition && Gap == other.Gap
        && CollectionValues.SequenceEqual(SenseContent, other.SenseContent) && CollectionValues.SequenceEqual(Front, other.Front)
        && CollectionValues.SequenceEqual(Back, other.Back);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (string value in new[] { Expression, Answer, Sentence, Translation, Explanation, Notes, GapPrompt, GapAnswer }) hash.Add(CollectionValues.StringHash(value));
        hash.Add(Recognition); hash.Add(Gap); hash.Add(CollectionValues.SequenceHash(Ranges)); hash.Add(CollectionValues.SequenceHash(SenseContent));
        hash.Add(CollectionValues.SequenceHash(Front)); hash.Add(CollectionValues.SequenceHash(Back)); return hash.ToHashCode();
    }
}

/// <summary>A resolved side owns literal text/blocks or a typed audio slot. Keeping audio typed
/// until rendering prevents user text resembling a placeholder from becoming an audio command.</summary>
public abstract record ReviewResolvedPart
{
    private ReviewResolvedPart() { }
    public sealed record Text(string Value) : ReviewResolvedPart
    {
        public bool Equals(Text? other) => other is not null && CollectionValues.Same(Value, other.Value);
        public override int GetHashCode() => CollectionValues.StringHash(Value);
    }
    public sealed record Blocks : ReviewResolvedPart
    {
        public Blocks(IReadOnlyList<VocabularyContentBlock> values) => Values = CollectionValues.List(values);
        public IReadOnlyList<VocabularyContentBlock> Values { get; }
        public bool Equals(Blocks? other) => other is not null && CollectionValues.SequenceEqual(Values, other.Values);
        public override int GetHashCode() => CollectionValues.SequenceHash(Values);
    }
    public sealed record Audio(ReviewAudioKind Kind) : ReviewResolvedPart;
}

/// <summary>Original review preparation failures are user-facing fixed messages, not an OS,
/// endpoint or model diagnostic. Pure preparation can fail before any external operation exists.</summary>
public sealed class AnkiReviewException : Exception
{
    /// <summary>Unclassified diagnostics retain their literal message; presentation must not
    /// interpret it as a resource key even when it happens to equal an owned failure sentence.</summary>
    public AnkiReviewException(string message) : base(message) { }
    public AnkiReviewException(AnkiReviewFailure failure, ReviewContentSource? missingSource = null)
        : base(failure.SourceKey() + (missingSource is { } source ? " · " + source.Label() : ""))
    { Failure = failure; MissingSource = missingSource; }
    public AnkiReviewFailure? Failure { get; }
    public ReviewContentSource? MissingSource { get; }
}
