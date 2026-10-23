namespace Cotabby.Core.Vocabulary;

/// <summary>
/// A draft owns these detached PNG bytes and their immutable hash reference, adapting Mac's
/// VocabularyPreparedImage. Preparing does not persist media or change a notebook. Construction
/// alone does not establish validity: the native preparation/storage boundary verifies bytes.
/// Defensive copies keep later caller edits from changing an admitted draft's content identity.
/// </summary>
public sealed class VocabularyPreparedImage
{
    private readonly byte[] _data;
    public VocabularyPreparedImage(VocabularyImageReference reference, ReadOnlySpan<byte> data)
    { Reference = reference; _data = data.ToArray(); }
    public VocabularyImageReference Reference { get; }
    public byte[] CopyBytes() => (byte[])_data.Clone();
}
