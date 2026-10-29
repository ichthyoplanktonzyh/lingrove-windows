using System.IO;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Opens only the new canonical filename. The application owns and shares the returned
/// single Store; this factory neither discovers nor opens the old notebook path.</summary>
public static class FrozenVocabularyStoreFactory
{
    public const string CanonicalFileName = "vocabulary-v8.sqlite";
    public static async Task<ICanonicalVocabularyStore> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetFileName(path), CanonicalFileName, StringComparison.OrdinalIgnoreCase))
            throw new VocabularyException(VocabularyFailure.InvalidContent);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run<ICanonicalVocabularyStore>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = FrozenVocabularySQLiteDatabase.OpenFile(path);
            if (cancellationToken.IsCancellationRequested) { database.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
            return new FrozenVocabularyImportStore(database);
        }, CancellationToken.None).ConfigureAwait(false);
    }
}
