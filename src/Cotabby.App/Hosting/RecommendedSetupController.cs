using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Inference;
using Cotabby.Win32.Models;
using Cotabby.App.Settings;

namespace Cotabby.App.Hosting;

internal interface IRecommendedSetupHost
{
    AppSettings Settings { get; }
    event Action? SettingsApplied;
    void PersistSettings();
    void PersistSelectionWithoutLoading();
    bool IsCached(CotabbyModel model);
    Task DownloadCompletion(CotabbyModel model, IProgress<(long Bytes, long? Total)> progress, CancellationToken token);
    Task DownloadLearning(LearningDownloadableModel model, LocalModelTask role, IProgress<(long Bytes, long? Total)> progress, CancellationToken token);
}

internal sealed class RecommendedSetupHost(AppHost host, SynchronizationContext ui) : IRecommendedSetupHost
{
    public AppSettings Settings => host.Settings;
    public event Action? SettingsApplied { add => host.SettingsApplied += value; remove => host.SettingsApplied -= value; }
    public void PersistSettings() => host.PersistSettings();
    public void PersistSelectionWithoutLoading() => host.PersistRecommendedSelection();
    public bool IsCached(CotabbyModel model) => ModelDownloader.IsCached(model);
    public Task DownloadCompletion(CotabbyModel model, IProgress<(long Bytes, long? Total)> progress, CancellationToken token) =>
        host.Downloader.DownloadAsync(model, new Progress<ModelDownloader.Progress>(value => progress.Report((value.BytesDownloaded, value.TotalBytes))), token, () => host.AcknowledgeCompletionDownloadAsync(model));
    public Task DownloadLearning(LearningDownloadableModel model, LocalModelTask role, IProgress<(long Bytes, long? Total)> progress, CancellationToken token) =>
        host.LearningModels.DownloadAsync(model, new Progress<Cotabby.App.Learning.LearningModelTransferProgress>(value => progress.Report((value.BytesDownloaded, value.TotalBytes))), token, () => OnUiAsync(() => host.AcknowledgeModelMetadata(settings =>
        { settings.RecordLocalModelTask(model.Filename, role); settings.RecordLocalModelSource(model.Filename, model.DownloadUrl); })));
    private Task OnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(_ => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }, null);
        return completion.Task;
    }
}

/// <summary>App-lifetime first-use operation. It borrows the existing task-specific transfer
/// owners and persists only choices/receipt; it never loads weights or creates a runtime.</summary>
internal sealed class RecommendedSetupController : IAsyncDisposable
{
    private readonly IRecommendedSetupHost _host;
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _run;
    private Task _work = Task.CompletedTask;
    private bool _finishing, _disposed;
    private readonly Dictionary<LocalModelTask, string> _status = [];
    private readonly Dictionary<LocalModelTask, string> _statusModel = [];
    internal event Action? Changed;
    internal RecommendedSetupPolicy.Plan Plan { get; }
    internal bool Busy => _run is not null;
    internal Task CurrentWork => _work;
    internal string Status(LocalModelTask task, string identity) =>
        _statusModel.TryGetValue(task, out var model) && string.Equals(model, identity, StringComparison.OrdinalIgnoreCase)
            ? _status.GetValueOrDefault(task) ?? "Not started" : "Not checked";
    internal bool Applied => _host.Settings.RecommendedSetupApplied;
    internal bool NeedsApplication
    {
        get
        {
            var s = _host.Settings;
            return !s.RecommendedSetupApplied || (s.RecommendedCompletionTarget is null && s.Engine == EngineKind.Local
                    && !s.PowerModelSwitching && string.IsNullOrEmpty(s.ActiveModelId))
                || RecommendedSetupPolicy.NeedsLearningSelection(s.EffectiveLearningBackend == LearningTranslationBackend.Local, s.LearningLocalModel, s.LearningTranslationPowerSwitching)
                || RecommendedSetupPolicy.NeedsLearningSelection(s.LearningExplanationBackend == LearningModelBackend.Local, s.LearningExplanationLocalModel, s.LearningExplanationPowerSwitching)
                || !s.Enabled || s.Pause?.IsActive(DateTimeOffset.Now) == true || !s.LearningTranslationEnabled;
        }
    }

    internal RecommendedSetupController(IRecommendedSetupHost host, ulong memory)
    {
        _host = host; Plan = RecommendedSetupPolicy.Create(memory);
        _host.SettingsApplied += OnSettingsApplied;
        FinishPending();
    }

    internal void Apply()
    {
        if (Busy || _disposed) return;
        var s = _host.Settings;
        if (s.Engine == EngineKind.Local && !s.PowerModelSwitching && string.IsNullOrEmpty(s.ActiveModelId)
            && string.IsNullOrEmpty(s.RecommendedCompletionTarget))
        { s.RecommendedCompletionPrevious = s.ActiveModelId; s.RecommendedCompletionTarget = Plan.Completion.Id; }
        if (RecommendedSetupPolicy.NeedsLearningSelection(s.EffectiveLearningBackend == LearningTranslationBackend.Local, s.LearningLocalModel, s.LearningTranslationPowerSwitching))
        { s.LearningLocalModel = Plan.Translation.Filename; s.LearningLocalTemplate = LearningChatTemplate.Automatic; }
        if (RecommendedSetupPolicy.NeedsLearningSelection(s.LearningExplanationBackend == LearningModelBackend.Local, s.LearningExplanationLocalModel, s.LearningExplanationPowerSwitching))
        { s.LearningExplanationLocalModel = Plan.Explanation.Filename; s.LearningExplanationLocalTemplate = LearningChatTemplate.Automatic; }
        s.Enabled = true; s.Pause = null; s.LearningTranslationEnabled = true; s.RecommendedSetupApplied = true;
        // Re-enabling an already-started cached selection must not load weights as a side effect
        // of applying the recommendation; the first eligible local request owns that preparation.
        _host.PersistSelectionWithoutLoading();
        StartDownloads();
    }
    internal void Retry() { if (!Busy && !_disposed) StartDownloads(); }
    internal void Cancel() => _run?.Cancel();
    internal void UseManualConfiguration()
    {
        if (_host.Settings.RecommendedCompletionTarget is null) return;
        _host.Settings.RecommendedCompletionTarget = null; _host.Settings.RecommendedCompletionPrevious = null;
        _host.PersistSettings(); Changed?.Invoke();
    }

    private void StartDownloads()
    {
        var s = _host.Settings;
        var completionId = s.RecommendedCompletionTarget ?? (s.Engine == EngineKind.Local && !s.PowerModelSwitching ? s.ActiveModelId : null);
        var completion = ModelCatalog.All.FirstOrDefault(model => model.Id == completionId);
        var translation = !s.LearningTranslationPowerSwitching && s.EffectiveLearningBackend == LearningTranslationBackend.Local
            ? LearningRuntimeCatalog.TranslationModels.FirstOrDefault(model => string.Equals(model.Filename, s.LearningLocalModel, StringComparison.OrdinalIgnoreCase)) : null;
        var explanation = !s.LearningExplanationPowerSwitching && s.LearningExplanationBackend == LearningModelBackend.Local
            ? LearningRuntimeCatalog.ExplanationModels.FirstOrDefault(model => string.Equals(model.Filename, s.LearningExplanationLocalModel, StringComparison.OrdinalIgnoreCase)) : null;
        var run = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token); _run = run;
        _work = RunAsync(run, completion, translation, explanation); Changed?.Invoke();
    }
    private async Task RunAsync(CancellationTokenSource run, CotabbyModel? completion, LearningDownloadableModel? translation, LearningDownloadableModel? explanation)
    {
        try
        {
            var tasks = new List<Task>();
            if (completion is not null) tasks.Add(Transfer(LocalModelTask.Completion, completion.FileName, (progress, token) => _host.DownloadCompletion(completion, progress, token)));
            if (translation is not null) tasks.Add(LearningTransfer(LocalModelTask.Translation, translation));
            if (explanation is not null) tasks.Add(LearningTransfer(LocalModelTask.Explanation, explanation));
            await Task.WhenAll(tasks);
            FinishPending();
        }
        finally { if (ReferenceEquals(_run, run)) _run = null; run.Dispose(); Changed?.Invoke(); }

        Task LearningTransfer(LocalModelTask role, LearningDownloadableModel model) => Transfer(role, model.Filename, (progress, token) => _host.DownloadLearning(model, role, progress, token));
        async Task Transfer(LocalModelTask role, string filename, Func<IProgress<(long Bytes, long? Total)>, CancellationToken, Task> action)
        {
            _statusModel[role] = filename; _status[role] = "Downloading…"; Changed?.Invoke();
            var progress = new Progress<(long Bytes, long? Total)>(value =>
            {
                if (!ReferenceEquals(_run, run) || run.IsCancellationRequested || _status.GetValueOrDefault(role) is not { } current || !current.StartsWith("Downloading", StringComparison.Ordinal)) return;
                _status[role] = value.Total is > 0 ? $"Downloading · {Math.Clamp((int)(100d * value.Bytes / value.Total.Value), 0, 100)}%" : "Downloading…";
                Changed?.Invoke();
            });
            try { await action(progress, run.Token); _status[role] = "Installed"; if (role == LocalModelTask.Completion) FinishPending(); }
            catch (OperationCanceledException) when (run.IsCancellationRequested) { _status[role] = "Cancelled"; }
            catch (Exception) { _status[role] = "Download failed. Retry to try again."; }
            Changed?.Invoke();
        }
    }
    private void OnSettingsApplied() { FinishPending(); Changed?.Invoke(); }
    private void FinishPending()
    {
        if (_finishing || _disposed) return;
        var s = _host.Settings; string? target = s.RecommendedCompletionTarget;
        if (target is null) return;
        _finishing = true;
        try
        {
            bool valid = RecommendedSetupPolicy.CanFinishCompletion(s.RecommendedCompletionPrevious, s.ActiveModelId, target,
                s.Engine == EngineKind.Local, s.PowerModelSwitching);
            var model = ModelCatalog.All.FirstOrDefault(value => value.Id == target);
            valid &= model is not null;
            if (valid && !_host.IsCached(model!)) return;
            if (valid) s.ActiveModelId = target;
            s.RecommendedCompletionTarget = null; s.RecommendedCompletionPrevious = null;
            _host.PersistSelectionWithoutLoading();
        }
        finally { _finishing = false; }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _host.SettingsApplied -= OnSettingsApplied;
        _shutdown.Cancel(); try { await _work; } finally { _shutdown.Dispose(); }
    }
}
