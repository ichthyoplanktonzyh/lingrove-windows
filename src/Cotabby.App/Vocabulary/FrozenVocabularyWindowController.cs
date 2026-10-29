using System.Windows;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Review;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>AppHost owns one lazy canonical window and its retirement. The Store and reader are
/// borrowed; only explicit Show activates a window, and ordinary close hides it without resetting state.</summary>
internal sealed class FrozenVocabularyWindowController : IAsyncDisposable
{
    private readonly IVocabularyEntryImportStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly IVocabularyWordListReader _reader;
    private readonly IVocabularyNotebookListPreferences _preferences;
    private readonly object _gate = new();
    private IReadOnlyList<LearningLanguage> _languages;
    private FrozenVocabularyNotebookWindow? _window;
    private AnkiReviewFeature? _reviewFeature;
    private Action? _configureReview;
    private IVocabularyGraphPreferences? _graphPreferences;
    private IVocabularyClipboardImageTransfer? _clipboard;
    private (ILearningExplanationGenerating Service, ILearningDictionaryProviding Dictionary, ILearningLexicalText Lexical,
        Func<string, IReadOnlyList<LearningDictionaryWord>> UnknownWords, Action Configure)? _learning;
    private Task? _retirement;
    private bool _closed, _learningAttached;
    internal FrozenVocabularyWindowController(IVocabularyEntryImportStore store, Dispatcher dispatcher,
        IVocabularyWordListReader reader, IVocabularyNotebookListPreferences listPreferences, IReadOnlyList<LearningLanguage>? preferredLanguages = null)
    { _store = store; _dispatcher = dispatcher; _reader = reader; _preferences = listPreferences; _languages = Array.AsReadOnly((preferredLanguages ?? []).ToArray()); }
    internal FrozenVocabularyNotebookWindow? Window => _window;
    internal void Show(VocabularySelection? selection = null)
    {
        lock (_gate) if (_closed) return;
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => Show(selection)); return; }
        lock (_gate) if (_closed) return;
        _window ??= new FrozenVocabularyNotebookWindow(_store, _reader, _languages, listPreferences: _preferences, hideOnClose: true);
        if (_learning is { } learning && !_learningAttached) ApplyLearning(learning);
        if (_clipboard is { } clipboard) _window.AttachClipboard(clipboard);
        if (_graphPreferences is { } graph) _window.AttachGraphPreferences(graph);
        if (_reviewFeature is { } review && _configureReview is { } configure) _window.AttachReviewFeature(review, configure);
        _window.Reveal(selection); _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
    internal void AttachLearning(ILearningExplanationGenerating service, ILearningDictionaryProviding dictionary,
        ILearningLexicalText lexical, Func<string, IReadOnlyList<LearningDictionaryWord>> unknownWords, Action configure)
    {
        lock (_gate) { if (_closed) return; if (_learning is not null) throw new InvalidOperationException("Learning is already attached.");
            _learning = (service, dictionary, lexical, unknownWords, configure); }
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => { if (_learning is { } value) ApplyLearning(value); }); return; }
        if (_learning is { } value) ApplyLearning(value);
    }
    private void ApplyLearning((ILearningExplanationGenerating Service, ILearningDictionaryProviding Dictionary, ILearningLexicalText Lexical,
        Func<string, IReadOnlyList<LearningDictionaryWord>> UnknownWords, Action Configure) value)
    { if (!_closed && !_learningAttached && _window is { } window) { window.AttachLearning(value.Service, value.Dictionary, value.Lexical, value.UnknownWords, value.Configure); _learningAttached = true; } }
    internal void AttachClipboard(IVocabularyClipboardImageTransfer clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard); lock (_gate) { if (_closed) return; if (_clipboard is not null && !ReferenceEquals(_clipboard, clipboard)) throw new InvalidOperationException("Clipboard is already attached."); _clipboard = clipboard; }
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(ApplyClipboard); return; } ApplyClipboard();
    }
    private void ApplyClipboard() { IVocabularyClipboardImageTransfer? clipboard; lock (_gate) { if (_closed) return; clipboard = _clipboard; } if (clipboard is not null) _window?.AttachClipboard(clipboard); }
    internal void AttachGraphPreferences(IVocabularyGraphPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences); lock (_gate) { if (_closed) return; if (_graphPreferences is not null && !ReferenceEquals(_graphPreferences, preferences)) throw new InvalidOperationException("Graph preferences are already attached."); _graphPreferences = preferences; }
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(ApplyGraph); return; } ApplyGraph();
    }
    private void ApplyGraph() { IVocabularyGraphPreferences? preferences; lock (_gate) { if (_closed) return; preferences = _graphPreferences; } if (preferences is not null) _window?.AttachGraphPreferences(preferences); }
    internal void AttachReviewFeature(AnkiReviewFeature feature, Action configure)
    {
        lock (_gate) { if (_closed) return; if (_reviewFeature is not null && !ReferenceEquals(_reviewFeature, feature)) throw new InvalidOperationException("A review feature is already attached."); _reviewFeature = feature; _configureReview = configure; }
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => ApplyReview()); return; }
        ApplyReview();
    }
    private void ApplyReview()
    {
        AnkiReviewFeature? feature; Action? configure;
        lock (_gate) { if (_closed) return; feature = _reviewFeature; configure = _configureReview; }
        if (feature is not null && configure is not null) _window?.AttachReviewFeature(feature, configure);
    }
    internal void SetPreferredLanguages(IReadOnlyList<LearningLanguage> languages)
    {
        var retained = Array.AsReadOnly(languages.ToArray());
        lock (_gate) { if (_closed) return; _languages = retained; }
        if (!_dispatcher.CheckAccess()) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => ApplyLanguages()); return; }
        ApplyLanguages();
    }
    private void ApplyLanguages()
    {
        IReadOnlyList<LearningLanguage> languages; lock (_gate) { if (_closed) return; languages = _languages; }
        _window?.SetPreferredLanguages(languages);
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_retirement is not null) return new(_retirement);
            _closed = true; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _retirement = completion.Task;
        }
        _ = CompleteRetirementAsync(completion); return new(completion.Task);
    }
    private async Task CompleteRetirementAsync(TaskCompletionSource completion)
    {
        try { await RetireAsync(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private async Task RetireAsync()
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            if (_window is not null) throw new InvalidOperationException("Retire the vocabulary window before dispatcher shutdown.");
            return;
        }
        if (_dispatcher.CheckAccess()) { if (_window is not null) await _window.RetireAsync(); }
        else await (await _dispatcher.InvokeAsync(() => _window?.RetireAsync() ?? Task.CompletedTask)).ConfigureAwait(false);
        _window = null;
    }
}
