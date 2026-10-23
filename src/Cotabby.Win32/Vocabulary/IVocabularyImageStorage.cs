using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Synchronous media effects borrowed only by the serialized archive. The Windows composition
/// supplies WIC/private-file storage; portable archive tests may omit media rather than claiming
/// native image execution. Read returns detached bytes, Write distinguishes new files from repairs,
/// and Remove is best-effort cleanup. The archive owns locks, transactions, lifetime and worker use.
/// </summary>
internal interface IVocabularyImageStorage
{
    VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken);
    byte[] Read(VocabularyImageReference image);
    bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image);
    void Remove(VocabularyImageReference image);
}
