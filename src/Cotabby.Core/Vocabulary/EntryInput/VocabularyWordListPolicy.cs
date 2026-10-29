using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.EntryInput;

public enum VocabularyWordListFormat { Csv, Text }
public enum VocabularyImportField { Expression, Meaning, DictionaryForm, Language, Kind, MeaningLanguage, Note, Tags, Example, ExampleTranslation }
public sealed record VocabularyWordListRow(int Line, IReadOnlyList<string> Cells);
public sealed record VocabularyWordList(VocabularyWordListFormat Format, IReadOnlyList<VocabularyWordListRow> Rows)
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Cells.Count);
}
public sealed record VocabularyWordListConfiguration(bool HasHeader, IReadOnlyDictionary<VocabularyImportField, int> Columns,
    LearningLanguage Language = LearningLanguage.English, VocabularyEntryKind Kind = VocabularyEntryKind.Word,
    LearningLanguage? MeaningLanguage = null);

/// <summary>Bounded, value-only word-list decoding and column mapping; no file handles or writes.</summary>
public static class VocabularyWordListPolicy
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumRows = 10_000;
    private const int MaximumColumns = 64;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<VocabularyImportField, string[]> Aliases = new()
    {
        [VocabularyImportField.Expression] = ["expression", "word", "term", "front", "词条", "单词", "词语", "表达"],
        [VocabularyImportField.Meaning] = ["meaning", "definition", "translation", "back", "释义", "含义", "翻译"],
        [VocabularyImportField.DictionaryForm] = ["dictionaryform", "lemma", "原形", "词典形式"],
        [VocabularyImportField.Language] = ["language", "lang", "语言"],
        [VocabularyImportField.Kind] = ["type", "kind", "类型"],
        [VocabularyImportField.MeaningLanguage] = ["meaninglanguage", "释义语言"],
        [VocabularyImportField.Note] = ["notes", "note", "备注", "笔记"],
        [VocabularyImportField.Tags] = ["tags", "tag", "标签"],
        [VocabularyImportField.Example] = ["example", "sentence", "examplesentence", "例句"],
        [VocabularyImportField.ExampleTranslation] = ["exampletranslation", "sentencetranslation", "例句译文", "例句翻译"],
    };

    public static VocabularyWordList Decode(ReadOnlySpan<byte> data, string suffix, CancellationToken cancellationToken = default)
    {
        if (data.Length > MaximumBytes) throw Error(VocabularyWordListError.TooLarge);
        bool csv = string.Equals(suffix, "csv", StringComparison.OrdinalIgnoreCase);
        if (!csv && !string.Equals(suffix, "txt", StringComparison.OrdinalIgnoreCase)) throw Error(VocabularyWordListError.UnsupportedFile);
        string text;
        try
        {
            bool le = data.StartsWith(new byte[] { 0xff, 0xfe });
            bool be = data.StartsWith(new byte[] { 0xfe, 0xff });
            text = le || be ? new UnicodeEncoding(be, true, true).GetString(data[2..]) : Utf8.GetString(data);
        }
        catch (DecoderFallbackException) { throw Error(VocabularyWordListError.Encoding); }
        if (text.Contains('\0')) throw Error(VocabularyWordListError.Encoding);
        if (text.StartsWith('\ufeff')) text = text[1..];
        var rows = csv ? CsvRows(Utf8.GetBytes(text), cancellationToken) : TextRows(text, cancellationToken);
        if (rows.Count == 0) throw Error(VocabularyWordListError.Empty);
        return new(csv ? VocabularyWordListFormat.Csv : VocabularyWordListFormat.Text, rows.AsReadOnly());
    }

    public static VocabularyWordListConfiguration Configuration(VocabularyWordList table, LearningLanguage language)
    {
        var columns = new Dictionary<VocabularyImportField, int> { [VocabularyImportField.Expression] = 0 };
        if (table.Format != VocabularyWordListFormat.Csv || table.Rows.Count == 0) return new(false, columns, language);
        string[] names = table.Rows[0].Cells.Select(HeaderName).ToArray();
        var mapped = new Dictionary<VocabularyImportField, int>();
        foreach (var field in Enum.GetValues<VocabularyImportField>())
        {
            int index = Array.FindIndex(names, name => Aliases[field].Contains(name, StringComparer.Ordinal));
            if (index >= 0) mapped[field] = index;
        }
        if (mapped.ContainsKey(VocabularyImportField.Example) && !mapped.ContainsKey(VocabularyImportField.ExampleTranslation))
        {
            int index = Array.FindIndex(names, name => name is "translation" or "译文" or "翻译");
            if (index >= 0)
            {
                if (mapped.TryGetValue(VocabularyImportField.Meaning, out int meaning) && meaning == index) mapped.Remove(VocabularyImportField.Meaning);
                mapped[VocabularyImportField.ExampleTranslation] = index;
            }
        }
        bool header = mapped.ContainsKey(VocabularyImportField.Expression);
        if (header) columns = mapped;
        else if (table.ColumnCount > 1) columns[VocabularyImportField.Meaning] = 1;
        return new(header, columns, language);
    }

    public static IReadOnlyList<VocabularyEntryInput> Inputs(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.Columns.TryGetValue(VocabularyImportField.Expression, out int expression)
            || expression < 0 || expression >= table.ColumnCount || configuration.Columns.Values.Any(i => i < 0 || i >= table.ColumnCount))
            throw Error(VocabularyWordListError.Mapping);
        var rows = table.Rows.Skip(configuration.HasHeader ? 1 : 0).ToArray();
        if (rows.Length == 0) throw Error(VocabularyWordListError.Empty);
        if (rows.Length > MaximumRows) throw Error(VocabularyWordListError.TooLarge);
        var result = new List<VocabularyEntryInput>(rows.Length);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string Cell(VocabularyImportField field) => configuration.Columns.TryGetValue(field, out int i) && i < row.Cells.Count ? FoundationText.TrimWhitespaceAndNewlines(row.Cells[i]) : "";
            string word = Cell(VocabularyImportField.Expression), example = Cell(VocabularyImportField.Example), translation = Cell(VocabularyImportField.ExampleTranslation);
            if (word.Length == 0) throw Error(VocabularyWordListError.MissingExpression, row.Line);
            if (example.Length == 0 && translation.Length != 0) throw Error(VocabularyWordListError.MissingExample, row.Line);
            string languageCode = Cell(VocabularyImportField.Language);
            LearningLanguage? language = string.Equals(languageCode, "und", StringComparison.OrdinalIgnoreCase) ? null : ParseLanguage(languageCode, configuration.Language, row.Line);
            LearningLanguage? meaningLanguage = ParseLanguage(Cell(VocabularyImportField.MeaningLanguage), configuration.MeaningLanguage, row.Line);
            string kindText = Cell(VocabularyImportField.Kind);
            VocabularyEntryKind kind = configuration.Kind;
            if (kindText.Length > 0) kind = kindText.ToLowerInvariant() switch { "word" => VocabularyEntryKind.Word, "expression" => VocabularyEntryKind.Expression, "pattern" => VocabularyEntryKind.Pattern, "sentence" => VocabularyEntryKind.Sentence, _ => throw Error(VocabularyWordListError.Kind, row.Line) };
            result.Add(VocabularyEntryInputPolicy.Validated(new(word, Cell(VocabularyImportField.DictionaryForm), language, kind,
                Cell(VocabularyImportField.Meaning), meaningLanguage, Cell(VocabularyImportField.Note),
                Cell(VocabularyImportField.Tags).Split([';', '；', ',', '，']), example, translation)));
        }
        return result.AsReadOnly();
    }

    private static LearningLanguage? ParseLanguage(string value, LearningLanguage? fallback, int line)
    {
        if (value.Length == 0) return fallback;
        foreach (var language in LearningLanguages.All)
            if (string.Equals(language.Code(), value, StringComparison.OrdinalIgnoreCase)) return language;
        throw Error(VocabularyWordListError.Language, line);
    }

    private static string HeaderName(string value) => string.Concat(new GraphemeString(value.ToLowerInvariant()).Where(g => g.IsLetter || g.IsNumber).Select(g => g.ToString())).Normalize(NormalizationForm.FormC);
    private static VocabularyWordListException Error(VocabularyWordListError error, int? line = null) => new(error, line);

    private static readonly char[] WhitespaceCharacters = "\u0009\u0020\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u200b\u202f\u205f\u3000".ToCharArray();

    private static List<VocabularyWordListRow> TextRows(string text, CancellationToken token)
    {
        var rows = new List<VocabularyWordListRow>();
        int start = 0, number = 0;
        while (start < text.Length)
        {
            token.ThrowIfCancellationRequested(); number++;
            int end = start;
            while (end < text.Length && text[end] is not ('\n' or '\r' or '\u0085' or '\u2028' or '\u2029')) end++;
            string expression = text[start..end].Trim(WhitespaceCharacters);
            if (expression.Length != 0)
            {
                if (rows.Count >= MaximumRows) throw Error(VocabularyWordListError.TooLarge);
                rows.Add(new(number, Array.AsReadOnly(new[] { expression })));
            }
            start = end < text.Length ? end + 1 : end;
            if (end < text.Length && text[end] == '\r' && start < text.Length && text[start] == '\n') start++;
        }
        return rows;
    }

    private enum CsvState { Plain, Quoted, Closed }
    private static List<VocabularyWordListRow> CsvRows(byte[] bytes, CancellationToken token)
    {
        var rows = new List<VocabularyWordListRow>(); var cells = new List<string>(); var field = new List<byte>();
        CsvState state = CsvState.Plain; int line = 1, startLine = 1;
        void FinishCell()
        {
            if (cells.Count >= MaximumColumns || field.Count > 262_144) throw Error(VocabularyWordListError.TooLarge);
            cells.Add(Utf8.GetString(field.ToArray())); field.Clear(); state = CsvState.Plain;
        }
        void FinishRow()
        {
            FinishCell();
            if (cells.Any(cell => FoundationText.TrimWhitespaceAndNewlines(cell).Length != 0))
            {
                // One additional source row permits a header plus 10,000 entries.
                if (rows.Count > MaximumRows) throw Error(VocabularyWordListError.TooLarge);
                rows.Add(new(startLine, Array.AsReadOnly(cells.ToArray())));
            }
            cells.Clear();
        }
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i % 4096 == 0) token.ThrowIfCancellationRequested();
            byte b = bytes[i];
            if (state == CsvState.Quoted)
            {
                if (b == 34)
                {
                    if (i + 1 < bytes.Length && bytes[i + 1] == 34) { field.Add(34); i++; }
                    else state = CsvState.Closed;
                }
                else { field.Add(b); if (b == 10 || (b == 13 && (i + 1 == bytes.Length || bytes[i + 1] != 10))) line++; }
            }
            else if (b == 44) FinishCell();
            else if (b is 10 or 13)
            {
                FinishRow(); if (b == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10) i++;
                line++; startLine = line;
            }
            else if (b == 34)
            {
                if (state != CsvState.Plain || field.Count != 0) throw Error(VocabularyWordListError.MalformedCsv, line);
                state = CsvState.Quoted;
            }
            else if (state == CsvState.Closed)
            {
                if (b is not (32 or 9)) throw Error(VocabularyWordListError.MalformedCsv, line);
            }
            else field.Add(b);
            if (field.Count > 262_144) throw Error(VocabularyWordListError.TooLarge);
        }
        token.ThrowIfCancellationRequested();
        if (state == CsvState.Quoted) throw Error(VocabularyWordListError.MalformedCsv, startLine);
        if (field.Count != 0 || cells.Count != 0 || state == CsvState.Closed) FinishRow();
        return rows;
    }
}
