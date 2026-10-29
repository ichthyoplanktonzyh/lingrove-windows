using System.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Source's conservative per-entry allocation budget, checked before projection.</summary>
public static class CliReadBudget
{
    public static void Validate(F.VocabularyEntry entry, int maximum = CliInputContract.MaximumBytes)
    {
        long remaining = maximum;
        void Take(long count) { if (count < 0 || count > remaining) throw new CliException(CliErrorCode.InvalidInput, "Entry exceeds the CLI read budget. Use the app's export."); remaining -= count; }
        void Text(string value) { long count = Encoding.UTF8.GetByteCount(value); if (count > remaining / 6) throw new CliException(CliErrorCode.InvalidInput, "Entry exceeds the CLI read budget. Use the app's export."); Take(count * 6 + 128); }
        void Meanings(IEnumerable<VocabularyMeaning> values) { foreach (var value in values) { Take(512); Text(value.Text); } }
        void Explanations(IEnumerable<F.LearningExplanationRecord> values) { foreach (var value in values) { Take(512); Text(value.Result.Meaning); Text(value.Result.Explanation); } }
        Take(4096); Text(entry.Expression); Text(entry.DictionaryForm);
        foreach (var sense in entry.Senses)
        {
            Take(2048); Text(sense.Meaning); Text(sense.Note); foreach (string tag in sense.Tags) Text(tag);
            Meanings(sense.StandaloneMeanings); Meanings(sense.AlternateMeanings); Explanations(sense.Explanations);
            foreach (var block in sense.Content ?? []) { Take(2048); if (block.Content is VocabularyBlockContent.Text text) Text(text.Value.Text); else if (block.Content is VocabularyBlockContent.Image image) Text(image.Caption); }
            foreach (var usage in sense.Examples)
            {
                Take(2048); Text(usage.Text); Text(usage.Translation); Text(usage.Meaning); Take((long)usage.Ranges.Count * 128);
                Meanings(usage.TranslationVariants); Explanations(usage.Explanations);
            }
        }
    }
}
