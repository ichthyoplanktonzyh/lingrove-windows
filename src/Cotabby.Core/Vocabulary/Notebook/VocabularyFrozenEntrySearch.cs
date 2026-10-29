using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Vocabulary.Notebook;

/// <summary>Frozen VocabularyPolicy.matches over collected entries; platform supplies localized containment.</summary>
public static class VocabularyFrozenEntrySearch
{
    public static bool Matches(F.VocabularyEntry entry, string query, IVocabularyTextSearching search)
    {
        var fields = new List<string> { entry.Expression, entry.DictionaryForm };
        foreach (var sense in entry.Senses)
        {
            fields.Add(sense.Meaning); fields.Add(sense.Note); fields.AddRange(sense.Tags);
            fields.AddRange(sense.StandaloneMeanings.Select(x => x.Text)); fields.AddRange(sense.AlternateMeanings.Select(x => x.Text));
            foreach (var block in sense.Content ?? []) fields.Add(block.Content switch { VocabularyBlockContent.Text text => text.Value.Text, VocabularyBlockContent.Image image => image.Caption, _ => "" });
            foreach (var example in sense.Examples)
            { fields.Add(example.Text); fields.Add(example.Translation); fields.Add(example.Meaning); fields.AddRange(example.TranslationVariants.Select(x => x.Text)); }
        }
        return GraphemeString.Words(query).All(term => fields.Any(field => search.Contains(field, term)));
    }
}
