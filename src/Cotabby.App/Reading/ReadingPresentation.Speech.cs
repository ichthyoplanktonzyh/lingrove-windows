using Cotabby.App.Speech;
using Cotabby.Core.Speech;

namespace Cotabby.App.Reading;

internal sealed partial class ReadingPresentation
{
    private readonly Guid _speechOwner = Guid.NewGuid();
    private LearningSpeechController? _speech;
    private SpeechTarget SourceSpeechTarget => new(_speechOwner, "preview.source");
    private SpeechTarget ResultSpeechTarget => new(_speechOwner, "preview.translation");
    internal void AttachSpeech(LearningSpeechController speech)
    {
        _dispatcher.VerifyAccess(); if (_speech is not null) throw new InvalidOperationException("Speech is already attached.");
        _speech = speech; _reading.AttachSpeech(speech, _speechOwner); _reading.SpeechRequested += SpeechRequested;
    }
    private void SpeechRequested(bool source) => Track(SpeakAsync(source));
    private async Task SpeakAsync(bool source)
    {
        var state = _readingState; var token = _token; long revision = _revision; var speech = _speech;
        if (state is null || speech is null || state.Status == ReadingStatus.Error || !source && state.Status != ReadingStatus.Ready) return;
        string text = source ? state.Selection.Text : state.Text; if (string.IsNullOrWhiteSpace(text)) return;
        string identity = speech.ExportConfigurationId;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
        if (!await StillCurrentAsync(revision, token) || !ReferenceEquals(state, _readingState) || state.Selection.Admission?.Invoke() == false || speech != _speech || identity != speech.ExportConfigurationId) return;
        speech.Toggle(new(source ? SourceSpeechTarget : ResultSpeechTarget, text, source ? state.Source : state.Target));
    }
    private void ReconcileSpeech(ReadingState next)
    {
        if (_readingState is not { } old) return;
        if (next.Status == ReadingStatus.Error || old.Selection != next.Selection || old.Source != next.Source) _speech?.Stop(SourceSpeechTarget);
        if (next.Status != ReadingStatus.Ready || old.Text != next.Text || old.Target != next.Target || old.Selection != next.Selection) _speech?.Stop(ResultSpeechTarget);
    }
    private void StopSpeech() => _speech?.Stop(_speechOwner);
    private void DetachSpeech() { _reading.SpeechRequested -= SpeechRequested; _reading.DetachSpeech(); _speech = null; }
}
