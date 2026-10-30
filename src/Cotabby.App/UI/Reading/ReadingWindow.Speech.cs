using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Cotabby.App.Reading;
using Cotabby.App.Speech;
using Cotabby.Core.Speech;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Reading;

internal sealed partial class ReadingWindow
{
    private readonly Button _sourceSpeech = new() { Content = InterfaceText.Get("Read source", "Learning"), Visibility = Visibility.Collapsed, Focusable = false, Margin = new(0, 0, 8, 0) };
    private readonly Button _resultSpeech = new() { Content = InterfaceText.Get("Read result", "Learning"), Visibility = Visibility.Collapsed, Focusable = false };
    private readonly TextBlock _speechStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 6), Visibility = Visibility.Collapsed };
    private LearningSpeechController? _speech;
    private Guid _speechOwner;
    private ReadingState? _speechState;
    internal event Action<bool>? SpeechRequested;
    private SpeechTarget SourceSpeechTarget => new(_speechOwner, "preview.source");
    private SpeechTarget ResultSpeechTarget => new(_speechOwner, "preview.translation");
    private void InitializeSpeech(Panel stack)
    {
        var row = new WrapPanel(); row.Children.Add(_sourceSpeech); row.Children.Add(_resultSpeech); stack.Children.Add(row); stack.Children.Add(_speechStatus);
        foreach (var button in new[] { _sourceSpeech, _resultSpeech }) button.SetResourceReference(StyleProperty, "Btn");
        _speechStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); AutomationProperties.SetLiveSetting(_speechStatus, AutomationLiveSetting.Polite);
        _sourceSpeech.Click += (_, _) => SpeechRequested?.Invoke(true); _resultSpeech.Click += (_, _) => SpeechRequested?.Invoke(false);
    }
    internal void AttachSpeech(LearningSpeechController speech, Guid owner)
    {
        if (_speech is not null) throw new InvalidOperationException("Speech is already attached.");
        _speech = speech; _speechOwner = owner; speech.PropertyChanged += SpeechChanged; RefreshSpeech();
    }
    internal void DetachSpeech() { if (_speech is { } speech) speech.PropertyChanged -= SpeechChanged; _speech = null; ClearSpeech(); }
    private void SpeechChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => RefreshSpeech();
    private void UpdateSpeech(ReadingState state) { _speechState = state; RefreshSpeech(); }
    private void ClearSpeech() { _speechState = null; RefreshSpeech(); }
    private void RefreshSpeech()
    {
        var state = _speechState; bool source = _speech is not null && state is { Status: not ReadingStatus.Error } && !string.IsNullOrWhiteSpace(state.Selection.Text);
        bool result = _speech is not null && state is { Status: ReadingStatus.Ready } && !string.IsNullOrWhiteSpace(state.Text);
        _sourceSpeech.Visibility = source ? Visibility.Visible : Visibility.Collapsed; _resultSpeech.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
        _sourceSpeech.Content = _speech?.IsPlaying(SourceSpeechTarget) == true ? InterfaceText.Get("Stop source", "Learning") : InterfaceText.Get("Read source", "Learning");
        _resultSpeech.Content = _speech?.IsPlaying(ResultSpeechTarget) == true ? InterfaceText.Get("Stop result", "Learning") : InterfaceText.Get("Read result", "Learning");
        _speechStatus.Text = _speech?.Error(SourceSpeechTarget) ?? _speech?.Error(ResultSpeechTarget) ?? ((source || result) ? _speech?.DestinationDescription : null) ?? "";
        _speechStatus.Visibility = _speechStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
