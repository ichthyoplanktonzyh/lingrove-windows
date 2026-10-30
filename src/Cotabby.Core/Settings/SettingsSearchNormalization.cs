using System.Text;
using Cotabby.Core.Text;

namespace Cotabby.Core.Settings;

/// <summary>Foundation's case/diacritic comparison projection for the search adapter. Canonical
/// decomposition removes Roman/Greek/Cyrillic accents only; kana/Indic marks and width remain.
/// Unicode full case mapping is borrowed from the platform, not replaced by simple lowercase.
/// Existing Darwin data is reused only for its grapheme-extend property, not editor search folds.</summary>
public static class SettingsSearchNormalization
{
    public static string Fold(string value, Func<string, string> fullCaseFold)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(fullCaseFold);
        var result = new StringBuilder(value.Length);
        for (int position = 0; position < value.Length;)
        {
            if (!Rune.TryGetRuneAt(value, position, out var original)) throw new ArgumentException("Malformed search text.", nameof(value));
            position += original.Utf16SequenceLength;
            if (original.Value == 0) { result.Append('\0'); continue; }
            string source = original.ToString(), decomposed = FoundationUnicodeNormalization.Normalize(source, NormalizationForm.FormD);
            Rune first = Rune.GetRuneAt(decomposed, 0);
            if (first.Value < 0x0510) source = first.ToString();
            // The unchanged Mac ranker uses default Unicode I folding even under tr_TR;
            // this was measured with real Foundation, rather than inferred from Locale.current.
            string folded = source.Length == 1 && source[0] < 128
                ? source.ToLowerInvariant() : fullCaseFold(source);
            bool removeMarks = Rune.GetRuneAt(folded, 0).Value < 0x0510;
            foreach (var scalar in folded.EnumerateRunes())
                if (folded == source || !removeMarks || !FoundationEditorTextData.IsExtend(scalar.Value)) result.Append(scalar.ToString());
            while (position < value.Length && Rune.TryGetRuneAt(value, position, out var mark) && FoundationEditorTextData.IsExtend(mark.Value))
            {
                if (!removeMarks) result.Append(FoundationUnicodeNormalization.Normalize(mark.ToString(), NormalizationForm.FormD));
                position += mark.Utf16SequenceLength;
            }
        }
        // Swift String/Character comparisons are canonically equivalent. Canonical comparison
        // keys do not alter the caller's labels, query or their stored UTF-16 identity.
        return FoundationUnicodeNormalization.Normalize(result.ToString().ToLowerInvariant());
    }
}
