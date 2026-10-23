namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Locale-aware substring facts, corresponding to Foundation localizedStandardContains on Mac.
/// The platform adapter owns Unicode/collation behavior; notebook policy owns searchable fields
/// and all-term matching. It must not log, mutate or transmit either string. One browser borrows it.
/// </summary>
public interface IVocabularyTextSearching
{
    bool Contains(string field, string term);
}
