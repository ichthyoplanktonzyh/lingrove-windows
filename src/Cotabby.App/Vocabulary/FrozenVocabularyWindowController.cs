using System.Windows;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;

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
    private Task? _retirement;
    private bool _closed;
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
        _window.Reveal(selection); _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
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
