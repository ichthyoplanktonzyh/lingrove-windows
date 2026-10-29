using System.IO;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Opens only the new canonical filename. The application owns and shares the returned
/// single Store; this factory neither discovers nor opens the old notebook path.</summary>
public static class FrozenVocabularyStoreFactory
{
    public const string CanonicalFileName = "vocabulary-v8.sqlite";
    /// <summary>Create one inert owner; no directory or database is opened before an admitted operation.</summary>
    public static ICanonicalVocabularyStore Create(string path)
    {
        if (!string.Equals(Path.GetFileName(path), CanonicalFileName, StringComparison.OrdinalIgnoreCase))
            throw new VocabularyException(VocabularyFailure.InvalidContent);
        string retained = VocabularyWindowsFile.FullPath(path);
        return new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(retained));
    }
    /// <summary>Eager convenience for explicitly requested open; failures dispose the candidate owner.</summary>
    public static async Task<ICanonicalVocabularyStore> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = Create(path);
        try { _ = await store.ReadAsync(cancellationToken).ConfigureAwait(false); return store; }
        catch { await store.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
