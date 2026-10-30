using System.ComponentModel;
using System.Windows.Threading;
using Cotabby.App.Speech;
using Cotabby.App.Vocabulary;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>One panel's borrowed learning tools. Detached candidate captures are prepared once per
/// request/result and remain stable across row navigation or Store notifications. Accepted saves
/// are joined before this lease retires; the shared Store and speech controller are never disposed.</summary>
internal sealed class ExpressionAssistCandidateTools : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly VocabularyCaptureFactory? _factory;
    private readonly Func<bool> _canAct;
    private readonly Func<ExpressionAssistCandidate, Task<bool>> _preflight;
    private readonly HashSet<Task> _work = [];
    private readonly Guid _speechOwner = Guid.NewGuid();
    private ExpressionAssistRequest? _request;
    private ExpressionAssistResult? _result;
    private VocabularyCapture?[] _captures = [];
    private long _revision;
    private bool _closed;
    private Task? _disposal;
    internal IWritingVocabularyStore? Vocabulary { get; }
    internal LearningSpeechController? Speech { get; }
    internal event Action? Changed;
    internal string? PreparationError { get; private set; }

    internal ExpressionAssistCandidateTools(Dispatcher dispatcher, IWritingVocabularyStore? vocabulary,
        VocabularyCaptureFactory? factory, LearningSpeechController? speech, Func<bool> canAct,
        Func<ExpressionAssistCandidate, Task<bool>> preflight)
    {
        dispatcher.VerifyAccess(); _dispatcher = dispatcher; Vocabulary = vocabulary; _factory = factory;
        Speech = speech; _canAct = canAct; _preflight = preflight;
        if (speech is not null) speech.PropertyChanged += SpeechChanged;
    }
    internal void Update(ExpressionAssistRequest? request, ExpressionAssistResult? result)
    {
        _dispatcher.VerifyAccess(); if (_closed) return;
        if (ReferenceEquals(_request, request) && ReferenceEquals(_result, result)) return;
        _request = request; _result = result; _captures = []; PreparationError = null; long revision = ++_revision;
        Changed?.Invoke();
        if (request is not null && result is not null && Vocabulary is not null && _factory is not null)
            Track(PrepareAsync(request, result, revision));
    }
    private async Task PrepareAsync(ExpressionAssistRequest request, ExpressionAssistResult result, long revision)
    {
        // No process basename is invented as an app bundle identity. The hypothetical context
        // records generated provenance, not a claim that these words were inserted in the host.
        var captures = await Task.Run(() => result.Candidates.Select(candidate =>
        {
            try { return _factory!.FromExpressionAssist(candidate, request); }
            catch { return null; }
        }).ToArray());
        if (_closed || revision != _revision || !ReferenceEquals(result, _result)) return;
        _captures = captures;
        PreparationError = captures.Any(capture => capture is null) ? "Some expressions cannot be saved because their language or context could not be verified." : null;
        Changed?.Invoke();
    }
    internal bool Current(int index, ExpressionAssistCandidate candidate) => !_closed && _canAct()
        && _result is { } result && index >= 0 && index < result.Candidates.Count && result.Candidates[index] == candidate;
    internal VocabularyCapture? Capture(int index, ExpressionAssistCandidate candidate) => Current(index, candidate)
        && index < _captures.Length ? _captures[index] : null;
    internal async Task<bool> ValidateAsync(int index, ExpressionAssistCandidate candidate)
    {
        long revision = _revision;
        return Current(index, candidate) && await _preflight(candidate) && revision == _revision && Current(index, candidate);
    }
    internal SpeechTarget Target(int index) => new(_speechOwner, "expression.candidate." + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal void ToggleSpeech(int index, ExpressionAssistCandidate candidate)
    {
        _dispatcher.VerifyAccess(); if (!Current(index, candidate) || Speech is null) return;
        string text = Cotabby.Core.Text.FoundationText.TrimWhitespaceAndNewlines(candidate.Text); if (text.Length == 0) return;
        // Like the original panel, an explicit click gives the shared controller a detached
        // request. Already accepted playback can outlive the panel; it is not host editing work.
        Speech.Toggle(new(Target(index), text, _request is { } request ? LearningLanguages.FromCode(request.TargetLanguage) : null));
    }
    private void SpeechChanged(object? sender, PropertyChangedEventArgs args) { if (!_closed) Changed?.Invoke(); }
    internal void Track(Task task)
    {
        _dispatcher.VerifyAccess(); _work.Add(task);
        _ = ObserveAsync(task);
    }
    private async Task ObserveAsync(Task task)
    {
        try { await task; } catch { }
        finally { _work.Remove(task); }
    }
    public ValueTask DisposeAsync()
    {
        _dispatcher.VerifyAccess();
        if (_disposal is not null) return new(_disposal);
        _closed = true; ++_revision; _request = null; _result = null; _captures = [];
        if (Speech is not null) Speech.PropertyChanged -= SpeechChanged;
        Changed = null;
        return new(_disposal = DrainAsync());
    }
    private async Task DrainAsync()
    {
        while (_work.Count > 0)
        {
            var work = _work.ToArray();
            try { await Task.WhenAll(work); } catch { }
            foreach (var task in work) _work.Remove(task);
        }
    }
}
