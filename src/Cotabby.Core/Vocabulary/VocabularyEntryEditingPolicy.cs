using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Value-draft rules from the stable Mac entry/content editors. A modal editor owns the full
/// draft and original baseline; these helpers neither mutate Store nor acknowledge persistence.
/// Original-text edits preserve annotations and re-anchor only ranges; IDs/provenance remain stable until
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

    /// <summary>Pure counterpart of source-text onChange, including its Swift-equivalent equality scheduling guard.</summary>
    public static VocabularyExample ChangeOriginalText(VocabularyExample example, string text, string expression, string dictionaryForm = "")
    {
        if (CanonicalStringComparer.Instance.Equals(example.Text, text)) return example with { Text = text };
        return example with { Text = text, Ranges = VocabularyPolicy.ReconciledRanges(text, expression, dictionaryForm) };
    }

    /// <summary>Expression onChange re-anchors examples belonging only to the editor's selected sense.</summary>
    public static VocabularyEntry ChangeExpression(VocabularyEntry draft, Guid senseId, string expression)
    {
        if (CanonicalStringComparer.Instance.Equals(draft.Expression, expression)) return draft with { Expression = expression };
        return draft with { Expression = expression, Senses = draft.Senses.Select(sense => sense.Id != senseId ? sense : sense with
        {
            Examples = sense.Examples.Select(example => example with
            { Ranges = VocabularyPolicy.ReconciledRanges(example.Text, expression, draft.DictionaryForm) }).ToArray(),
        }).ToArray() };
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
