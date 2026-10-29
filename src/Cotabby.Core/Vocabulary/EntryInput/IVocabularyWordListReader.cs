namespace Cotabby.Core.Vocabulary.EntryInput;

/// <summary>Borrowed file effect. Implementations admit only bounded regular files and return
/// detached values; the caller owns cancellation and preview sequencing.</summary>
public interface IVocabularyWordListReader
{
    Task<VocabularyWordList> ReadAsync(string path, CancellationToken cancellationToken);
}
