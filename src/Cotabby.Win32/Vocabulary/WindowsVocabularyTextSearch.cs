using System.Globalization;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Windows linguistic search adapter for Foundation's case/diacritic-insensitive locale search.
/// This managed boundary captures the UI culture without IO. OS/runtime collation can differ;
/// source fixtures explicitly distinguish policy parity from native text-provider parity.
/// Durable spelling and identity keys are never folded through this presentation-only search.
/// </summary>
public sealed class WindowsVocabularyTextSearch(CultureInfo? culture = null) : IVocabularyTextSearching
{
    private readonly CompareInfo _comparison = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
    public bool Contains(string field, string term)
    {
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        // Foundation does not turn a literal all-ignorable query (for example U+200B) into an
        // empty match. Preserve that case instead of letting IndexOf report every field at zero.
        return _comparison.Compare(term, "", options) == 0 ? field.Contains(term, StringComparison.Ordinal)
            : _comparison.IndexOf(field, term, options) >= 0;
    }
}
