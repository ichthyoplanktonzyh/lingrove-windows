using System.Windows;
using Cotabby.App.Speech;
using Cotabby.App.Localization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.App.UI.Learning;

public partial class LearningPreviewWindow
{
    private readonly Guid _speechOwner = Guid.NewGuid();
    private LearningSpeechController? _speech;
    private SpeechTarget SourceTarget => new(_speechOwner, "preview.source");
    private SpeechTarget ResultTarget => new(_speechOwner, "preview.translation");
    internal void AttachSpeech(LearningSpeechController speech)
    {
        Dispatcher.VerifyAccess();
        if (_speech is not null) throw new InvalidOperationException("Speech is already attached.");
        _speech = speech; speech.PropertyChanged += SpeechChanged;
        SourceSpeechButton.Click += (_, _) => SpeakSource(); ResultSpeechButton.Click += (_, _) => SpeakResult();
        Closed += (_, _) => { StopSpeech(); speech.PropertyChanged -= SpeechChanged; _speech = null; };
        UpdateSpeech();
    }
    internal void RetireExampleSpeech()
    {
        if (!_embeddedExample || _speech is not { } speech) return;
        StopSpeech(); speech.PropertyChanged -= SpeechChanged; _speech = null; UpdateSpeech();
    }
    private void SpeechChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => UpdateSpeech();
    private void StopSpeech() => _speech?.Stop(_speechOwner);
    private void ReconcileSpeech(LearningPreviewState old, LearningPreviewState next)
    {
        if (next.Status is not (LearningPreviewStatus.Ready or LearningPreviewStatus.Loading) || old.Source != next.Source || old.Options?.SourceLanguage != next.Options?.SourceLanguage) _speech?.Stop(SourceTarget);
        if (next.Status != LearningPreviewStatus.Ready || old.Result != next.Result || old.Options?.TargetLanguage != next.Options?.TargetLanguage) _speech?.Stop(ResultTarget);
    }
    private void UpdateSpeech()
    {
        var state = _displayedPreview; bool source = _speech is not null && state is { Status: LearningPreviewStatus.Ready or LearningPreviewStatus.Loading } && state.Source.Length > 0;
        bool result = _speech is not null && state is { Status: LearningPreviewStatus.Ready, Result.Length: > 0, Options: not null };
        SourceSpeechButton.Visibility = source ? Visibility.Visible : Visibility.Collapsed; ResultSpeechButton.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
        SourceSpeechButton.IsEnabled = source;
        SourceSpeechButton.Content = _speech?.IsPlaying(SourceTarget) == true ? "■" : "🔊"; ResultSpeechButton.Content = _speech?.IsPlaying(ResultTarget) == true ? "■" : "🔊";
        SourceSpeechButton.ToolTip = _speech?.Error(SourceTarget) ?? InterfaceText.Get("Play source text aloud", "Learning"); ResultSpeechButton.ToolTip = _speech?.Error(ResultTarget) ?? InterfaceText.Get("Play translation aloud", "Learning");
        SpeechStatus.Text = _speech?.Error(SourceTarget) ?? _speech?.Error(ResultTarget) ?? ((source || result) ? _speech?.DestinationDescription : null) ?? ""; SpeechStatus.Visibility = SpeechStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(SourceSpeechButton, InterfaceText.Get(_speech?.IsPlaying(SourceTarget) == true ? "Stop speaking source text" : "Play source text aloud", "Learning"));
        System.Windows.Automation.AutomationProperties.SetName(ResultSpeechButton, InterfaceText.Get(_speech?.IsPlaying(ResultTarget) == true ? "Stop speaking translation" : "Play translation aloud", "Learning"));
    }
    private void SpeakSource() { if (_displayedPreview is { } state && _speech is { } speech) speech.Toggle(new(SourceTarget, state.Source, state.Options?.SourceLanguage)); }
    private void SpeakResult() { if (_displayedPreview is { Status: LearningPreviewStatus.Ready, Options: { } options, Result: { Length: > 0 } text } && _speech is { } speech) speech.Toggle(new(ResultTarget, text, options.TargetLanguage)); }
}
