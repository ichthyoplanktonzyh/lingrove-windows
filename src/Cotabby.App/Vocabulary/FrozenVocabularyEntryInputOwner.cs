using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Vocabulary;

/// <summary>Presentation borrows the one app-owned Frozen Store. This adapter maps exact
/// acknowledged counts/identities and never owns or disposes the Store itself.</summary>
internal sealed class FrozenVocabularyEntryInputOwner(IVocabularyEntryImportStore store) : IVocabularyEntryInputOwner
{
    public async Task<VocabularyInputPreview> PreviewAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken token)
    {
        var preview = await store.PreviewWordListAsync(table, configuration, token).ConfigureAwait(false);
        return new(preview.Inputs, new(preview.Revision, Map(preview.Result)));
    }
    public async Task<VocabularyInputResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token) =>
        Map(await store.AddEntriesAsync(inputs, token).ConfigureAwait(false));
    public IDisposable Subscribe(Action<ulong> changed) => store.Subscribe(snapshot => changed(snapshot.Revision));
    private static VocabularyInputResult Map(VocabularyEntryImportResult value) => new(value.AddedEntries, value.AddedSenses, value.SkippedRows,
        value.AddedExamples, value.AddedTranslations, value.ChangedCollections,
        value.Selection is { } selection ? new(selection.EntryId, selection.SenseId) : null);
}
