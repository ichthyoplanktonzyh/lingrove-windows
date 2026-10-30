using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Review;

/// <summary>Logical review material becomes user-mapped Anki fields only at explicit preparation.
/// These rules preserve identity, remote overrides and three-way conflicts; the controller owns
/// scope checks and writes. Audio inputs here are receipt markup, never synthesis or file access.</summary>
public static partial class AnkiReviewPolicy
{
    public const string PresetName = "Lingrove v1";
    public static IReadOnlyDictionary<ReviewField, string> PresetMapping { get; } = CollectionValues.Map(new Dictionary<ReviewField, string>
    {
        [ReviewField.Identity] = "LingroveID", [ReviewField.Expression] = "Expression", [ReviewField.Meaning] = "Meaning",
        [ReviewField.Sentence] = "Sentence", [ReviewField.Translation] = "Translation", [ReviewField.Explanation] = "Explanation",
        [ReviewField.Notes] = "Notes", [ReviewField.Recognition] = "Recognition", [ReviewField.GapPrompt] = "GapPrompt",
        [ReviewField.GapAnswer] = "GapAnswer", [ReviewField.ExpressionAudio] = "ExpressionAudio", [ReviewField.SentenceAudio] = "SentenceAudio",
    });
    public static IReadOnlyDictionary<string, string> Fields(ReviewCardContent content, Guid identity,
        IReadOnlyDictionary<ReviewField, string> mapping, IReadOnlyDictionary<string, string> overrides,
        IReadOnlyDictionary<ReviewField, string>? audio = null)
    {
        if (mapping.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null)) throw new AnkiReviewException(AnkiReviewFailure.InvalidMapping);
        var selected = mapping.Where(pair => pair.Value.Length != 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (!selected.ContainsKey(ReviewField.Identity) || !selected.ContainsKey(ReviewField.Expression) || !selected.ContainsKey(ReviewField.Meaning)
            || selected.Values.Distinct(CanonicalStringComparer.Instance).Count() != selected.Count
            || selected.Values.Any(name => !LearningExplanationText.Fits(name, 200) || name.Any(c => ScalarSets.IsNewline(c))))
            throw new AnkiReviewException(AnkiReviewFailure.InvalidMapping);
        var values = ReviewCardPolicy.Values(content, identity).ToDictionary(pair => pair.Key, pair => pair.Value);
        audio ??= new Dictionary<ReviewField, string>();
        foreach (var pair in audio)
        {
            if (!Enum.IsDefined(pair.Key)) throw new AnkiReviewException(AnkiReviewFailure.InvalidMapping);
            values[pair.Key] = pair.Value;
        }
        if (content.Front is not null && content.Back is not null)
        {
            values[ReviewField.Expression] = ReviewLayoutPolicy.Html(content.Front, audio);
            values[ReviewField.Meaning] = ReviewLayoutPolicy.Html(content.Back, audio);
        }
        var result = new Dictionary<string, string>(CanonicalStringComparer.Instance);
        foreach (var pair in selected) result.Add(pair.Value, values.GetValueOrDefault(pair.Key) ?? "");
        // The original protects the selected identity field, including canonically equivalent
        // spelling. Overrides of unselected fields cannot make an update touch other model fields.
        foreach (var pair in overrides)
            if (result.ContainsKey(pair.Key) && !CollectionValues.Same(pair.Key, selected[ReviewField.Identity])) result[pair.Key] = pair.Value;
        long count = 0;
        foreach (string value in result.Values)
        {
            if (!LearningExplanationText.Fits(value, 1_000_000)) throw new AnkiReviewException(AnkiReviewFailure.ReviewContentTooLarge);
            count += System.Text.Encoding.UTF8.GetByteCount(value);
        }
        if (count > 1_000_000) throw new AnkiReviewException(AnkiReviewFailure.ReviewContentTooLarge);
        return CollectionValues.Map(result);
    }
    public static IReadOnlyList<AnkiFieldConflict> Conflicts(IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> local, IReadOnlyDictionary<string, string> remote)
    {
        // Own comparison maps with Foundation key equality without altering retained key/text
        // spelling. A remote-only edit is deliberately visible rather than silently acknowledged.
        var original = CollectionValues.Map(baseline); var ours = CollectionValues.Map(local); var theirs = CollectionValues.Map(remote);
        return CollectionValues.List(ours.Keys.Order(CanonicalStringComparer.Instance).Select(key =>
        {
            string before = original.GetValueOrDefault(key) ?? "", mine = ours[key], incoming = theirs.GetValueOrDefault(key) ?? "";
            return !CollectionValues.Same(incoming, before) && !CollectionValues.Same(incoming, mine) ? new AnkiFieldConflict(key, mine, incoming) : null;
        }).OfType<AnkiFieldConflict>());
    }
    public static bool RemovesExistingCard(ReviewPlan plan, AnkiNoteBinding binding)
    {
        if (binding.NoteId is null) return false;
        string Field(ReviewField field) => binding.Fields.GetValueOrDefault(binding.Mapping.GetValueOrDefault(field) ?? "") ?? "";
        return !plan.Recognition && Field(ReviewField.Recognition).Length != 0 || !plan.Gap && Field(ReviewField.GapPrompt).Length != 0;
    }
    public static IReadOnlyDictionary<ReviewField, string> AudioFields(ReviewPlan plan)
    {
        var result = new Dictionary<ReviewField, string>();
        foreach (var kind in plan.AudioKinds ?? new HashSet<ReviewAudioKind>())
        {
            ReviewField field = kind.Field();
            if (plan.AudioReceipts?.TryGetValue(kind, out var receipt) == true) result.Add(field, "[sound:" + receipt.Filename + "]");
        }
        return CollectionValues.Map(result);
    }
}
