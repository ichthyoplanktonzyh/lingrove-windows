using System.IO;
using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>AppHost's single canonical notebook owner. The writing bridge and activating library
/// borrow the same lazy backend; neither creates or disposes a second archive. Construction has
/// no notebook IO. AppHost drains explanation producers before disposing this owner.</summary>
internal sealed class CanonicalVocabularyFeature : IAsyncDisposable
{
    private readonly ICanonicalVocabularyStore _backend;
    private readonly WindowsVocabularyClipboardTransfer _clipboard = new();
    private readonly object _gate = new();
    private Task? _disposal;
    internal CanonicalWritingViewStore Writing { get; }
    internal FrozenVocabularyWindowController Window { get; }

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cotabby", "vocabulary", FrozenVocabularyStoreFactory.CanonicalFileName);
    internal void AttachGraphPreferences(IVocabularyGraphPreferences preferences) => Window.AttachGraphPreferences(preferences);

    internal CanonicalVocabularyFeature(string path, SynchronizationContext ui, Dispatcher dispatcher,
        IVocabularyNotebookListPreferences listPreferences, IReadOnlyList<LearningLanguage> preferredLanguages)
    {
        _backend = FrozenVocabularyStoreFactory.Create(path);
        Writing = new(_backend, ui);
        Window = new(_backend, dispatcher, new WindowsVocabularyWordListReader(), listPreferences, preferredLanguages);
        Window.AttachClipboard(_clipboard);
        Writing.OpenSelection = Window.Show;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= RetireAsync());
    }

    private async Task RetireAsync()
    {
        try { await Window.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            Writing.OpenSelection = null;
            Writing.EnrichCapture = null;
            try { await Writing.DisposeAsync().ConfigureAwait(false); }
            finally { try { await _clipboard.DisposeAsync().ConfigureAwait(false); } finally { await _backend.DisposeAsync().ConfigureAwait(false); } }
        }
    }
}
