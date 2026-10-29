using System.Windows;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Dictionaries;

/// <summary>One application-owned catalog, local book service and router, borrowed by all
/// dictionary sessions. Its window and accepted file work drain before the service is released.</summary>
internal sealed class DictionaryFeature : IAsyncDisposable
{
    private readonly DictionarySettings _settings; private readonly MDictLibraryService _books;
    private readonly DictionaryRouter _router; private readonly List<Task> _retired = [];
    private DictionarySettingsWindow? _window; private bool _closed; private Task? _dispose;
    internal DictionaryFeature(Func<AppSettings> current, Action persist, ILearningDictionaryProviding bundled, Func<string, string> lowercase,
        Func<string, LearningLanguage?, IReadOnlyList<LearningDictionaryWord>> words)
    { _settings = new(current, persist); _books = new(lowercase); _router = new(_settings, bundled, _books, words); }
    internal ILearningDictionaryProviding Router => _router;
    internal void ApplySettings() { if (!_closed) _settings.Reload(); }
    internal void ShowSettings(Window? owner = null)
    {
        if (_closed) return;
        if (_window is { } existing) { existing.Activate(); return; }
        var window = new DictionarySettingsWindow(_settings, _books); if (owner is { IsVisible: true }) window.Owner = owner;
        _window = window; window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; _retired.Add(window.DisposeAsync().AsTask()); };
        window.Show();
    }
    public ValueTask DisposeAsync() => new(_dispose ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        _closed = true; var window = _window; if (window is not null) { window.Close(); await window.DisposeAsync(); }
        await Task.WhenAll(_retired); await _books.DisposeAsync();
    }
}
