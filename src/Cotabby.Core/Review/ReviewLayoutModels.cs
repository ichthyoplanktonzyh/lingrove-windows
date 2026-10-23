using System.Text.Json.Serialization;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Stable layout source slots, separate from resolved/exported material and labels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewContentSource>))]
public enum ReviewContentSource
{
    [JsonStringEnumMemberName("expression")] Expression,
    [JsonStringEnumMemberName("meaning")] Meaning,
    [JsonStringEnumMemberName("sentence")] Sentence,
    [JsonStringEnumMemberName("translation")] Translation,
    [JsonStringEnumMemberName("contextualMeaning")] ContextualMeaning,
    [JsonStringEnumMemberName("explanation")] Explanation,
    [JsonStringEnumMemberName("notes")] Notes,
    [JsonStringEnumMemberName("gapSentence")] GapSentence,
    [JsonStringEnumMemberName("senseText")] SenseText,
    [JsonStringEnumMemberName("senseImage")] SenseImage,
    [JsonStringEnumMemberName("expressionAudio")] ExpressionAudio,
    [JsonStringEnumMemberName("sentenceAudio")] SentenceAudio,
    [JsonStringEnumMemberName("custom")] Custom,
}

/// <summary>
/// An ordered front/back layout retained by a review plan or template. Projections preserve part
/// and custom-content order, while audio kinds deliberately form a set. No template application,
/// source-slot binding or media preparation happens merely by constructing this value.
/// </summary>
public sealed record ReviewLayout
{
    private IReadOnlyList<ReviewCardPart> _front = Array.Empty<ReviewCardPart>(), _back = Array.Empty<ReviewCardPart>();
    public ReviewLayout(IReadOnlyList<ReviewCardPart>? front = null, IReadOnlyList<ReviewCardPart>? back = null)
    { Front = front ?? []; Back = back ?? []; }
    public IReadOnlyList<ReviewCardPart> Front { get => _front; init => _front = CollectionValues.List(value); }
    public IReadOnlyList<ReviewCardPart> Back { get => _back; init => _back = CollectionValues.List(value); }
    public IReadOnlyList<ReviewCardPart> Parts => CollectionValues.List(Front.Concat(Back));
    public IReadOnlyList<VocabularyContentBlock> CustomContent => CollectionValues.List(Parts.SelectMany(part => part.Content));
    public IReadOnlySet<ReviewAudioKind> AudioKinds => CollectionValues.OptionalSet(Parts.Where(part =>
        part.Source is ReviewContentSource.ExpressionAudio or ReviewContentSource.SentenceAudio).Select(part =>
        part.Source == ReviewContentSource.ExpressionAudio ? ReviewAudioKind.Expression : ReviewAudioKind.Sentence))!;
    public bool Equals(ReviewLayout? other) => other is not null && CollectionValues.SequenceEqual(Front, other.Front)
        && CollectionValues.SequenceEqual(Back, other.Back);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.SequenceHash(Front), CollectionValues.SequenceHash(Back));
}

/// <summary>
/// A plan-owned slot references notebook content by stable BlockId or owns card-only content.
/// Null BlockId is an unbound template slot, not permission to infer an image by caption/order.
/// Explicit creation generates an ID; clones/edits retain the existing ID and detached content.
/// </summary>
public sealed record ReviewCardPart
{
    private IReadOnlyList<VocabularyContentBlock> _content = Array.Empty<VocabularyContentBlock>();
    public ReviewCardPart(Guid id, ReviewContentSource source, Guid? blockId = null, bool showCaption = false,
        IReadOnlyList<VocabularyContentBlock>? content = null)
    { Id = id; Source = source; BlockId = blockId; ShowCaption = showCaption; Content = content ?? []; }
    public Guid Id { get; init; }
    public ReviewContentSource Source { get; init; }
    public Guid? BlockId { get; init; }
    public bool ShowCaption { get; init; }
    public IReadOnlyList<VocabularyContentBlock> Content { get => _content; init => _content = CollectionValues.List(value); }
    public static ReviewCardPart Create(ReviewContentSource source) => new(Guid.NewGuid(), source);
    public bool Equals(ReviewCardPart? other) => other is not null && Id == other.Id && Source == other.Source
        && BlockId == other.BlockId && ShowCaption == other.ShowCaption && CollectionValues.SequenceEqual(Content, other.Content);
    public override int GetHashCode() => HashCode.Combine(Id, Source, BlockId, ShowCaption, CollectionValues.SequenceHash(Content));
}

/// <summary>Notebook-owned reusable layout identity; applying it to a sense is a separate policy.</summary>
public sealed record ReviewLayoutTemplate(Guid Id, string Name, ReviewLayout Layout)
{
    public static ReviewLayoutTemplate Create(string name, ReviewLayout layout) => new(Guid.NewGuid(), name, layout);
    public bool Equals(ReviewLayoutTemplate? other) => other is not null && Id == other.Id
        && CollectionValues.Same(Name, other.Name) && Layout == other.Layout;
    public override int GetHashCode() => HashCode.Combine(Id, CollectionValues.StringHash(Name), Layout);
}
