using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Value-draft rules from the stable Mac entry/content editors. A modal editor owns the full
/// draft and original baseline; these helpers neither mutate Store nor acknowledge persistence.
/// Original-text edits retire dependent annotations, while IDs/provenance remain stable until
/// archive commit prunes stale generated records. Existing media blocks are retained verbatim.
/// </summary>
public static class VocabularyEntryEditingPolicy
{
    public static IReadOnlyList<string> ParseTags(string text) => CollectionValues.List(new GraphemeString(text)
        .Split(character => character.Is(',') || character.Is('，')).Select(range => LearningExplanationText.Trim(text[range]))
        .Where(tag => tag.Length > 0).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance));

    public static bool CanSave(VocabularyEntry draft, Guid senseId, bool saving = false, bool importingImage = false) =>
        !saving && !importingImage && LearningExplanationText.Trim(draft.Expression).Length > 0
        && draft.Senses.FirstOrDefault(value => value.Id == senseId) is { } selected
        && selected.Examples.All(example => LearningExplanationText.Trim(example.Text).Length > 0);

    /// <summary>Called by the source-text onChange event, after its Swift-equivalent equality check.</summary>
    public static VocabularyExample ChangeOriginalText(VocabularyExample example, string text)
    {
        if (CanonicalStringComparer.Instance.Equals(example.Text, text)) return example with { Text = text };
        return example with { Text = text, Ranges = [], Translation = "", TranslationLanguage = null, TranslationVariants = [],
            Meaning = "", MeaningLanguage = null, MeaningOrigin = VocabularyMeaningOrigin.Captured, Completeness = VocabularyExampleCompleteness.Fragment };
    }

    public static IReadOnlyList<VocabularyContentBlock>? MoveBlock(IReadOnlyList<VocabularyContentBlock>? blocks, Guid id, int offset)
    {
        if (blocks is null) return null;
        int index = -1; for (int position = 0; position < blocks.Count; position++) if (blocks[position].Id == id) { index = position; break; }
        long destination = (long)index + offset;
        if (index < 0 || destination < 0 || destination >= blocks.Count) return blocks;
        var values = blocks.ToArray(); (values[index], values[(int)destination]) = (values[(int)destination], values[index]);
        return CollectionValues.List(values);
    }
}
