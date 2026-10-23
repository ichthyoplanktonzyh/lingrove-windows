using System.Globalization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Mac TXT and CSV document traversal. The archive will call this on a captured portable graph;
/// one local bounded writer owns output bytes until return. No media, live notebook, file or model
/// is queried. The projected options decide content; the CSV options also decide columns and rows.
/// </summary>
public static partial class VocabularyExportFormatter
{
    public static byte[] RenderText(VocabularyExportDocument document, bool expressionList = false,
        VocabularyExportTextLabels? labels = null, CancellationToken cancellationToken = default)
    {
        labels ??= new(); using var output = new BoundedWriter(); var seen = new HashSet<string>(CanonicalStringComparer.Instance);
        foreach (var entry in document.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expressionList)
            {
                string expression = VocabularyCapturePolicy.Normalized(entry.Expression);
                if (seen.Add(entry.Language.Code() + "\0" + expression)) output.Append(expression + "\n");
                continue;
            }
            output.Append(entry.Expression + " [" + entry.Language.Code() + "]\n");
            if (!CollectionValues.Same(entry.DictionaryForm, entry.Expression)) output.Append(labels.DictionaryForm + ": " + entry.DictionaryForm + "\n");
            for (int index = 0; index < entry.Senses.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested(); var sense = entry.Senses[index];
                output.Append("\n" + labels.Sense + " " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + LanguageText(sense.Meaning ?? "", sense.MeaningLanguage) + "\n");
                if (sense.OtherMeanings is { } meanings) output.Append(MeaningsText(meanings) + (meanings.Count == 0 ? "" : "\n"));
                if (sense.Note is { Length: > 0 } note) output.Append(labels.Notes + ": " + note + "\n");
                if (sense.Tags is { Count: > 0 } tags) output.Append(labels.Tags + ": " + string.Join("; ", tags) + "\n");
                output.Append(BlockText(sense.Content ?? [], labels) + ExplanationText(sense.Explanations ?? []));
                foreach (var usage in sense.Examples ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Append("\n" + labels.ExampleSentence + ": " + usage.Text + "\n");
                    if (usage.Translation.Length != 0) output.Append(labels.ExampleTranslation + ": " + LanguageText(usage.Translation, usage.TranslationLanguage) + "\n");
                    output.Append(MeaningsText(usage.OtherTranslations) + (usage.OtherTranslations.Count == 0 ? "" : "\n"));
                    if (usage.Meaning is { Length: > 0 } meaning) output.Append(labels.ContextualMeaning + ": " + LanguageText(meaning, usage.MeaningLanguage) + "\n");
                    output.Append(ExplanationText(usage.Explanations ?? []));
                }
            }
            output.Append("\n────────────────────\n\n");
        }
        return output.Bytes();
    }
    public static byte[] RenderCsv(VocabularyExportDocument document, VocabularyExportConfiguration options, CancellationToken cancellationToken = default)
    {
        using var output = new BoundedWriter();
        if (options.SpreadsheetCompatibility) output.Append("\uFEFF");
        void Row(IEnumerable<string> values) => output.Append(string.Join(",", values.Select(value => CsvCell(value, options.SpreadsheetCompatibility))) + "\r\n");
        Row(CsvColumns(options));
        foreach (var entry in document.Entries)
        foreach (var sense in entry.Senses)
        {
            var examples = options.Examples ? sense.Examples ?? [] : [];
            // A sense with no enabled examples still emits one row. Disabling examples must not
            // duplicate that row once for every stored usage in the original notebook.
            IEnumerable<VocabularyExportDocument.Usage?> usages = examples.Count == 0 ? new VocabularyExportDocument.Usage?[] { null } : examples.Select(usage => (VocabularyExportDocument.Usage?)usage);
            foreach (var usage in usages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Row(CsvValues(entry, sense, usage, options));
            }
        }
        return output.Bytes();
    }
    private static IReadOnlyList<string> CsvColumns(VocabularyExportConfiguration options)
    {
        var columns = new List<string> { "entry_id", "expression", "dictionary_form", "language", "type", "sense_id", "saved_at" };
        if (options.Meanings) columns.AddRange(["meaning", "meaning_language", "other_meanings"]);
        if (options.Examples)
        {
            columns.AddRange(["usage_id", "usage_saved_at", "example", "translation", "translation_language", "other_translations"]);
            if (options.Meanings) columns.AddRange(["contextual_meaning", "contextual_meaning_language"]);
        }
        if (options.Notes) columns.AddRange(["notes", "tags"]);
        if (options.Explanation) columns.AddRange(["sense_explanations", "usage_explanations"]);
        if (options.Content) columns.AddRange(["supplemental_text", "images", "image_captions"]);
        return columns;
    }
    private static IEnumerable<string> CsvValues(VocabularyExportDocument.Entry entry, VocabularyExportDocument.Sense sense,
        VocabularyExportDocument.Usage? usage, VocabularyExportConfiguration options)
    {
        var values = new List<string> { entry.Id.ToString("D").ToUpperInvariant(), entry.Expression, entry.DictionaryForm, entry.Language.Code(),
            entry.Kind.ToString().ToLowerInvariant(), sense.Id.ToString("D").ToUpperInvariant(), IsoDate(sense.CreatedAt) };
        if (options.Meanings) values.AddRange([sense.Meaning ?? "", sense.MeaningLanguage?.Code() ?? "", MeaningsText(sense.OtherMeanings ?? [])]);
        if (options.Examples)
        {
            values.AddRange([usage?.Id.ToString("D").ToUpperInvariant() ?? "", usage is null ? "" : IsoDate(usage.CreatedAt), usage?.Text ?? "",
                usage?.Translation ?? "", usage?.TranslationLanguage?.Code() ?? "", MeaningsText(usage?.OtherTranslations ?? [])]);
            if (options.Meanings) values.AddRange([usage?.Meaning ?? "", usage?.MeaningLanguage?.Code() ?? ""]);
        }
        if (options.Notes) values.AddRange([sense.Note ?? "", string.Join("; ", sense.Tags ?? [])]);
        if (options.Explanation) values.AddRange([ExplanationText(sense.Explanations ?? []), ExplanationText(usage?.Explanations ?? [])]);
        if (options.Content)
        {
            var blocks = sense.Content ?? [];
            values.AddRange([string.Join("\n\n", blocks.Where(b => b.Text is not null).Select(b => b.Text!.Text)),
                string.Join("\n", blocks.Select(b => b.Filename).OfType<string>()), string.Join("\n", blocks.Select(b => b.Caption).OfType<string>())]);
        }
        return values;
    }
    private static string IsoDate(DateTimeOffset date) => date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string LanguageText(string text, LearningLanguage? language) => text.Length == 0 ? "" : (language is { } value ? "[" + value.Code() + "] " : "") + text;
    private static string MeaningsText(IReadOnlyList<VocabularyMeaning> meanings) => string.Join("\n", meanings.Select(m => (m.Language is { } language ? "[" + language.Code() + "] " : "") + m.Text));
    private static string ExplanationText(IReadOnlyList<VocabularyExportDocument.Explanation> records) => string.Join("\n", records.Select(r => "[" + r.Language.Code() + "] " + r.Meaning + "\n" + r.Detail + "\n"));
    private static string BlockText(IReadOnlyList<VocabularyExportDocument.Block> blocks, VocabularyExportTextLabels labels) => string.Concat(blocks.Select(b => b.Text is { } text ? text.Text + "\n" : labels.Image + ": " + (b.Filename ?? "") + (b.Caption is { Length: > 0 } caption ? " · " + caption : "") + "\n"));

    /// <summary>One render owns this byte buffer. Check UTF-8 size before encoding/appending each
    /// source-equivalent chunk; the BOM, headers, delimiters and newlines count toward the limit.</summary>
    private sealed class BoundedWriter : IDisposable
    {
        private readonly MemoryStream _bytes = new();
        internal void Append(string text)
        {
            int count = StrictUtf8.GetByteCount(text);
            if (count > VocabularyExportPolicy.MaximumDataBytes - _bytes.Length) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
            _bytes.Write(StrictUtf8.GetBytes(text));
        }
        internal byte[] Bytes() => _bytes.ToArray();
        public void Dispose() => _bytes.Dispose();
    }
}
