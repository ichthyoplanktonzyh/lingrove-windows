using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Hosting;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Onboarding;
using Cotabby.Core.Speech;
using Cotabby.Inference;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.UI;

internal enum ReadinessTask { Completion, Translation, Explanation, Speech }
internal sealed record ReadinessRow(ReadinessTask Task, string Detail, OnboardingModelStatus Status);
internal sealed record ReadinessSnapshot(EngineKind Engine, string CompletionId, string CompletionUrl, string CompletionModel,
    LearningTranslationBackend TranslationBackend, string TranslationLocal, string TranslationUrl, string TranslationModel,
    LearningModelSnapshot Explanation, LearningSpeechProviderKind SpeechProvider, string SpeechModel, string SpeechProjection,
    string SpeechReference, string SpeechUrl, string SpeechEndpointModel, string SpeechVoice,
    IReadOnlyDictionary<string, string[]> Assignments, bool CompletionFailed);

/// <summary>UI-owned readiness view: detached settings are inspected asynchronously, and every
/// refresh retires older work. It borrows download state and never starts inference or transfers.</summary>
internal sealed class WelcomeReadinessView : StackPanel
{
    private readonly AppHost? _host;
    private readonly Func<CancellationToken, Task<ReadinessRow[]>>? _testInspection;
    private readonly Action<ReadinessTask>? _testNavigate;
    internal Func<string, bool>? CompletionFailed { get; set; }
    private readonly Dictionary<ReadinessTask, (TextBlock Detail, TextBlock Status)> _rows = [];
    private readonly HashSet<Task> _work = [];
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private CancellationTokenSource? _current;
    private bool _closed;
    private int _epoch;
    private Task? _retirement;

    internal WelcomeReadinessView(AppHost host) : this()
    {
        _host = host;
        _host.SettingsApplied += Refresh;
        _host.Downloader.Activity.Changed += Changed;
        _host.LearningModels.Activity.Changed += Changed;
        _host.Speech.Controller.Settings.PropertyChanged += SpeechChanged;
    }
    internal WelcomeReadinessView(Func<CancellationToken, Task<ReadinessRow[]>> inspect, Action<ReadinessTask> navigate) : this()
    { _testInspection = inspect; _testNavigate = navigate; }
    private WelcomeReadinessView()
    {
        MaxWidth = 440; HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var task in Enum.GetValues<ReadinessTask>())
        {
            var detail = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var title = new TextBlock { Text = InterfaceText.Get(task switch { ReadinessTask.Completion => "Autocomplete", ReadinessTask.Translation => "Translation", ReadinessTask.Explanation => "AI explanation", _ => "Speech" }), FontWeight = FontWeights.SemiBold };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(new StackPanel { Children = { title, detail } }); Grid.SetColumn(status, 1); grid.Children.Add(status);
            var button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 3, 0, 3), MinHeight = 48 };
            button.SetResourceReference(StyleProperty, "Btn");
            button.SetResourceReference(BackgroundProperty, "CardBg");
            SizeChanged += (_, _) => grid.Width = Math.Max(0, ActualWidth - 26);
            button.Click += (_, _) => { if (_host is null) _testNavigate?.Invoke(task); else if (task == ReadinessTask.Speech) _host.ShowSpeechSettings(Window.GetWindow(this)); else SettingsWindow.ShowReadinessTask(_host, task); };
            Children.Add(button); _rows.Add(task, (detail, status));
        }
    }
    private void SpeechChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Changed();
    private void Changed() { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!_closed && IsVisible) Refresh(); }); }
    internal void VisibilityChanged()
    {
        if (IsVisible) Refresh();
        else { _epoch++; _current?.Cancel(); }
    }
    internal void Refresh()
    {
        if (_closed || !IsVisible) return;
        _current?.Cancel(); var cts = new CancellationTokenSource(); _current = cts; int epoch = ++_epoch;
        foreach (var row in _rows.Values) { row.Detail.Text = ""; row.Status.Text = InterfaceText.Get("Checking…"); row.Status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); }
        if (_host is null)
        {
            Track(RunAsync(_testInspection!, epoch, cts)); return;
        }
        var s = _host.Settings; var voice = _host.Speech.Controller.Settings;
        var learning = ActiveLearningSelections(s);
        var snapshot = new ReadinessSnapshot(s.Engine, s.RecommendedCompletionTarget ?? s.ActiveModelId ?? "", s.EndpointBaseUrl, s.EndpointModel,
            learning.Translation.Backend == LearningModelBackend.Endpoint ? LearningTranslationBackend.Endpoint : LearningTranslationBackend.Local,
            learning.Translation.LocalModel, s.LearningEndpointBaseUrl, learning.Translation.EndpointModel,
            learning.Explanation, voice.SelectedProvider, voice.LocalModelPath, voice.LocalProjectionPath, voice.ReferenceAudioPath,
            voice.EndpointBaseUrl, voice.EndpointModel, voice.EndpointVoice,
            s.ModelAssignmentSnapshot().ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal),
            CompletionFailed?.Invoke(s.RecommendedCompletionTarget ?? s.ActiveModelId ?? "") == true);
        Track(RunAsync(token => Task.Run(() => InspectAsync(snapshot, token), token), epoch, cts));
    }
    internal static (LearningPowerProfile Translation, LearningModelSnapshot Explanation) ActiveLearningSelections(AppSettings settings) =>
        (settings.LearningTranslationActiveSelection, settings.LearningExplanationSnapshot());
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(_ => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => _work.Remove(task)); }, TaskScheduler.Default);
    }
    private async Task RunAsync(Func<CancellationToken, Task<ReadinessRow[]>> inspect, int epoch, CancellationTokenSource cts)
    {
        try
        {
            await _scanGate.WaitAsync(cts.Token);
            ReadinessRow[] rows;
            try { rows = await inspect(cts.Token); }
            finally { _scanGate.Release(); }
            if (_closed || epoch != _epoch || cts.IsCancellationRequested) return;
            foreach (var row in rows)
            {
                var (detail, status) = _rows[row.Task]; detail.Text = row.Detail;
                status.Text = InterfaceText.Get(row.Status switch { OnboardingModelStatus.Configured => "Configured", OnboardingModelStatus.Downloading => "Downloading your model…", OnboardingModelStatus.Unavailable => "Needs attention", _ => "No model configured" });
                status.SetResourceReference(TextBlock.ForegroundProperty, row.Status == OnboardingModelStatus.Configured ? "GreenBrush" : "TextSecondary");
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closed && epoch == _epoch) foreach (var row in _rows.Values) row.Status.Text = InterfaceText.Get("Needs attention"); }
        finally { if (ReferenceEquals(_current, cts)) _current = null; cts.Dispose(); }
    }
    private async Task<ReadinessRow[]> InspectAsync(ReadinessSnapshot s, CancellationToken ct)
    {
        var host = _host!;
        ct.ThrowIfCancellationRequested();
        var completion = s.Engine == EngineKind.Local ? ModelCatalog.FindById(s.CompletionId, s.Assignments) : null;
        string completionName = s.Engine == EngineKind.Local ? completion?.FileName ?? s.CompletionId : s.CompletionModel.Trim();
        bool completionValid = s.Engine == EngineKind.Local ? completion is not null && ModelDownloader.IsCached(completion) && !s.CompletionFailed : EndpointValid(s.CompletionUrl, completionName);
        bool completionDownloading = completion is not null && host.Downloader.Activity.IsDownloading(completion.FileName);
        var models = await host.LearningModels.GetInstalledModelsAsync(ct).ConfigureAwait(false);
        bool Has(string name, LocalModelTask role) => models.Any(m => string.Equals(m.Filename, name, StringComparison.OrdinalIgnoreCase)) && LocalModelTaskPolicy.IsCandidate(name, role, LocalModelTaskAssignments.TasksFor(name, s.Assignments));
        string translation = (s.TranslationBackend == LearningTranslationBackend.Local ? s.TranslationLocal : s.TranslationModel).Trim();
        bool translationValid = s.TranslationBackend == LearningTranslationBackend.Local ? Has(translation, LocalModelTask.Translation) : EndpointValid(s.TranslationUrl, translation);
        var ex = s.Explanation; string explanation = (ex.Backend == LearningModelBackend.Local ? ex.LocalModel : ex.EndpointModel).Trim();
        bool explanationValid = ex.Backend == LearningModelBackend.Local ? Has(explanation, LocalModelTask.Explanation) && SupportsExplanation(ex, ct) : EndpointValid(ex.BaseUrl, explanation);
        string? speechError = SpeechError(s);
        ct.ThrowIfCancellationRequested();
        return [new(ReadinessTask.Completion, completionName, OnboardingSetupPolicy.ModelStatus(completionName.Length > 0, completionValid, completionDownloading)),
            new(ReadinessTask.Translation, translation, OnboardingSetupPolicy.ModelStatus(translation.Length > 0, translationValid, host.LearningModels.Activity.IsDownloading(translation))),
            new(ReadinessTask.Explanation, explanation, OnboardingSetupPolicy.ModelStatus(explanation.Length > 0, explanationValid, host.LearningModels.Activity.IsDownloading(explanation))),
            new(ReadinessTask.Speech, speechError ?? s.SpeechProvider.ToString(), speechError is null ? OnboardingModelStatus.Configured : OnboardingModelStatus.NeedsConfiguration)];
    }
    private bool SupportsExplanation(LearningModelSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(_host!.LearningModels.ModelsDirectory, snapshot.LocalModel);
            var metadata = LearningModelMetadataProbe.Read(path, ct);
            var resolved = LearningLocalPrompt.ResolveTemplate(LearningChatTemplate.Automatic, metadata, snapshot.LocalModel);
            return LearningExplanationModelPolicy.GeneralInstructionsAvailable(true, snapshot.LocalModel, snapshot.Template, resolved) == true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
    internal static bool EndpointValid(string url, string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        try { _ = new OpenAICompatibleEndpointConfiguration(url, model, OpenAICompatibleApiMode.ChatCompletions); return true; } catch { return false; }
    }
    private static string? SpeechError(ReadinessSnapshot s)
    {
        try
        {
            if (s.SpeechProvider == LearningSpeechProviderKind.Local) _ = LearningSpeechSettings.ValidateLocalConfiguration(s.SpeechModel, s.SpeechProjection, s.SpeechReference, new SpeechAssetFiles());
            if (s.SpeechProvider == LearningSpeechProviderKind.Endpoint && (!EndpointValid(s.SpeechUrl, s.SpeechEndpointModel) || string.IsNullOrWhiteSpace(s.SpeechVoice) || Encoding.UTF8.GetByteCount(s.SpeechVoice) > 256 || Encoding.UTF8.GetByteCount(s.SpeechEndpointModel) > 256)) return "Choose a speech endpoint, model and voice.";
            return null;
        }
        catch (SpeechException error) { return error.Message; }
        catch { return "Speech configuration is unavailable."; }
    }
    internal Task RetireAsync()
    {
        if (_retirement is not null) return _retirement;
        _closed = true; _epoch++; _current?.Cancel();
        if (_host is not null) { _host.SettingsApplied -= Refresh; _host.Downloader.Activity.Changed -= Changed; _host.LearningModels.Activity.Changed -= Changed; _host.Speech.Controller.Settings.PropertyChanged -= SpeechChanged; }
        return _retirement = Task.WhenAll(_work.ToArray());
    }
}
