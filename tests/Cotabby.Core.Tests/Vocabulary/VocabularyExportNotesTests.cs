using System.Globalization;
using System.Text;
using Cotabby.Core.Tests.Parity;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyExportNotesTests
{
    [Fact]
    public void TextRendererConsumesAllLocalizedLabelsFromOneValueSnapshot()
    {
        var entries = VocabularyExportProjectionParityTests.ExportEntries(); entries[0] = entries[0] with { Expression = "banks" };
        var labels = new VocabularyExportTextLabels { DictionaryForm = "dictionary-label", Sense = "sense-label", Notes = "notes-label", Tags = "tags-label",
            ExampleSentence = "sentence-label", ExampleTranslation = "translation-label", ContextualMeaning = "context-label", Image = "image-label" };
        string text = Encoding.UTF8.GetString(VocabularyExportFormatter.RenderText(VocabularyExportPolicy.Document(entries, new()), labels: labels));
        foreach (string label in new[] { labels.DictionaryForm, labels.Sense, labels.Notes, labels.Tags, labels.ExampleSentence, labels.ExampleTranslation, labels.ContextualMeaning, labels.Image })
            Assert.Contains(label, text, StringComparison.Ordinal);
    }
    [Fact]
    public void CurrentCultureDoesNotChangeDateUuidOrSenseNumberFormatting()
    {
        var document = VocabularyExportPolicy.Document(VocabularyExportProjectionParityTests.ExportEntries(), new());
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            byte[] text = VocabularyExportFormatter.RenderText(document), csv = VocabularyExportFormatter.RenderCsv(document, new());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            Assert.Equal(text, VocabularyExportFormatter.RenderText(document)); Assert.Equal(csv, VocabularyExportFormatter.RenderCsv(document, new()));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
