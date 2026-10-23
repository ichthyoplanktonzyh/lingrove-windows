namespace Cotabby.Win32.Vocabulary;

public sealed partial class VocabularyArchive
{
    /// <summary>
    /// Native composition is separate from portable transaction sequencing. Every production
    /// archive owns real WIC/media storage for the same path as SQLite, including isolated memory
    /// mode. Portable tests compile the transaction owner and inject actual memory SQLite directly;
    /// they do not compile this constructor or pretend to run a Windows codec on macOS.
    /// </summary>
    public VocabularyArchive(string? path) : this(() => path is null ? VocabularySQLiteDatabase.CreateMemory() : VocabularySQLiteDatabase.OpenFile(path),
        TimeProvider.System, new WindowsVocabularyImageStorage(path)) { }
}
