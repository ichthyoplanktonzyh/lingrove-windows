using System.ComponentModel;
using System.IO;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Speech;

/// <summary>The app environment owns one shared controller; cards submit detached targets and
/// explicit actions. It freezes settings/language before providers, scopes callbacks to one token,
/// and owns generation tasks independently of autocomplete/translation. Platform voices, codecs,
/// recognition/collation and lazy model management are required borrowed adapters, never fake
/// defaults. They are not constructed by views. One serial UI context owns all published state.
/// The host stops callers, shuts down, and drains accepted work before releasing dependencies.</summary>
internal sealed partial class LearningSpeechController : INotifyPropertyChanged, ISpeechDestinationDisclosure, IAsyncDisposable
{
    internal LearningSpeechSettings Settings { get; }
    internal SpeechTelemetry Telemetry { get; }
    internal IReadOnlyList<RuntimeModelOption> AvailableLocalModels { get; private set; } = Array.Empty<RuntimeModelOption>();
    internal IReadOnlyList<SpeechVoice> VoiceCatalog { get; private set; } = Array.Empty<SpeechVoice>();
    internal bool CatalogIsLoaded { get; private set; }
    internal string? VoiceCatalogError { get; private set; }
    internal SpeechTarget? ActiveTarget { get; private set; }
    internal SpeechTarget? ErrorTarget { get; private set; }
    internal string? PlaybackError { get; private set; }
    internal SpeechPlaybackPhase? Phase { get; private set; }
    public string DestinationDescription
    {
        get
        {
            RequireOwner();
            switch (Settings.SelectedProvider)
            {
                case LearningSpeechProviderKind.System: return "System voice · audio stays on this device.";
                case LearningSpeechProviderKind.Local: return "Local speech · audio stays on this device. Model: " + SafeModelName(Path.GetFileName(Settings.LocalModelPath));
                case LearningSpeechProviderKind.Endpoint:
                    try
                    {
                        var endpoint = Settings.EndpointConfiguration().Endpoint;
                        return "Speech endpoint: " + endpoint.BaseUrl.GetLeftPart(UriPartial.Authority) + " · model: " + SafeModelName(endpoint.ModelName) + ". The selected text is sent to this server.";
                    }
                    catch { return "Speech endpoint is not configured; no text can be sent."; }
                default: return "Speech destination is unavailable.";
            }
        }
    }
    private static string SafeModelName(string value)
    {
        if (value.Length == 0) return "not selected";
        if (value.Contains("://", StringComparison.Ordinal) || value.IndexOfAny(['?', '#', '@']) >= 0) return "configured model";
        var output = new StringBuilder(); int count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == 96) { output.Append('…'); break; }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator) output.Append(' ');
            else output.Append(rune.ToString());
        }
        return output.ToString();
    }
    private readonly ISpeechProviding _provider;
    private readonly ISpeechAudioGenerating _generator;
    private readonly ISpeechAudioPlaying _audioPlayer;
    private readonly ISpeechPCMPlaying _pcmPlayer;
    private readonly ISystemSpeechAudioExporting _systemExporter;
    private readonly ISpeechAudioDuration _duration;
    private readonly Func<string, IReadOnlyDictionary<string, double>> _recognize;
    private readonly IComparer<string> _voiceComparer;
    private readonly string _modelDirectory;
    private readonly Func<ISpeechModelManagement> _modelFactory;
    private readonly ISpeechAssetFiles _files;
    private ISpeechModelManagement? _modelDownloads;
    private readonly SynchronizationContext _context;
    private readonly int _ownerThread;
    private readonly PropertyChangedEventHandler _relayChanged;
    private readonly Action _settingsInvalidation;
    private readonly HashSet<ControllerWork> _work = [];
    private readonly List<Exception> _lifetimeErrors = [];
    private ControllerWork? _audioTask;
    private SpeechPlaybackToken? _activeToken;
    private SpeechRequest? _activeRequest;
    private SpeechPlaybackToken? _exportToken;
    private ulong _exportRevision;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    internal LearningSpeechController(LearningSpeechSettings settings, SpeechTelemetry telemetry,
        ISpeechProviding provider, ISpeechAudioGenerating generator, ISpeechAudioPlaying audioPlayer,
        ISpeechPCMPlaying pcmPlayer, ISystemSpeechAudioExporting systemExporter, ISpeechAudioDuration duration,
        Func<string, IReadOnlyDictionary<string, double>> recognize, IComparer<string> voiceComparer,
        string modelDirectory, Func<ISpeechModelManagement> modelFactory, ISpeechAssetFiles? files = null)
    {
        _context = SynchronizationContext.Current ?? throw new InvalidOperationException("Speech control requires a serial UI context.");
        _ownerThread = Environment.CurrentManagedThreadId;
        Settings = settings; Telemetry = telemetry; _provider = provider; _generator = generator; _audioPlayer = audioPlayer;
        _pcmPlayer = pcmPlayer; _systemExporter = systemExporter; _duration = duration; _recognize = recognize; _voiceComparer = voiceComparer;
        _modelDirectory = modelDirectory; _modelFactory = modelFactory; _files = files ?? new SpeechAssetFiles();
        var weak = new WeakReference<LearningSpeechController>(this); var callback = ProviderCallback(weak);
        _provider.EventHandler = callback; _audioPlayer.EventHandler = callback; _pcmPlayer.EventHandler = callback;
        (_relayChanged, _settingsInvalidation) = ObserverCallbacks(weak);
        Telemetry.PropertyChanged += _relayChanged; Settings.PropertyChanged += _relayChanged; Settings.PlaybackInvalidated += _settingsInvalidation;
    }
    private static Action<SpeechProviderEvent> ProviderCallback(WeakReference<LearningSpeechController> weak) =>
        value => { if (weak.TryGetTarget(out var owner)) owner.Receive(value); };
    private static (PropertyChangedEventHandler Changed, Action Invalidate) ObserverCallbacks(WeakReference<LearningSpeechController> weak) =>
        ((_, _) => { if (weak.TryGetTarget(out var owner)) owner.Changed(); }, () => { if (weak.TryGetTarget(out var owner)) owner.InvalidatePlayback(); });
    private static (Action Changed, Func<IReadOnlySet<string>> Protected) ModelCallbacks(WeakReference<LearningSpeechController> weak) =>
        (() => { if (weak.TryGetTarget(out var owner)) owner.RefreshLocalModels(); },
         () => weak.TryGetTarget(out var owner) ? owner.ProtectedFilenames() : new HashSet<string>(CanonicalStringComparer.Instance));
    private void InvalidatePlayback() { unchecked { _exportRevision++; } Stop(); }
    internal ISpeechModelManagement ModelDownloads
    {
        get
        {
            RequireOwner();
            if (_modelDownloads is null)
            {
                _modelDownloads = _modelFactory();
                var callbacks = ModelCallbacks(new WeakReference<LearningSpeechController>(this));
                _modelDownloads.DirectoryChanged = callbacks.Changed;
                _modelDownloads.ProtectedModelFilenames = callbacks.Protected;
            }
            return _modelDownloads;
        }
    }
    private IReadOnlySet<string> ProtectedFilenames()
    {
        RequireOwner();
        // Preferences may already name the next pair while the old helper still owns its files.
        // Keep actual resident names protected until a reaped runtime clears its PID.
        var names = new HashSet<string>(CanonicalStringComparer.Instance)
        { FileLeaf(Settings.LocalModelPath), FileLeaf(Settings.LocalProjectionPath) };
        if (Telemetry.Runtime.ProcessId is not null) names.UnionWith(Telemetry.Runtime.LoadedModelFilenames);
        names.Remove(""); return names;
    }
    private static string FileLeaf(string path) => Path.GetFileName(Path.GetFullPath(path.Length == 0 ? "." : path));
    internal void RefreshVoices()
    {
        RequireOwner();
        try { VoiceCatalog = Array.AsReadOnly(_provider.RefreshVoices().ToArray()); VoiceCatalogError = VoiceCatalog.Count == 0 ? "No system speech voices are available." : null; }
        catch (Exception error) { VoiceCatalog = Array.Empty<SpeechVoice>(); VoiceCatalogError = error.Message; }
        CatalogIsLoaded = true; Changed();
    }
    internal IReadOnlyList<SpeechVoice> Voices(LearningLanguage language) => SpeechVoicePolicy.Voices(VoiceCatalog, language, _voiceComparer);
    internal void Toggle(SpeechRequest request)
    {
        RequireOwner();
        if (_exportToken is not null) { SetError(new SpeechException(SpeechFailure.OperationInProgress).Message, request.Target); return; }
        if (IsPlaying(request.Target)) { Stop(request.Target); return; }
        Stop();
        if (!SpeechTextPolicy.WithinRequestLimit(request.Text))
        { SetError(new SpeechException(SpeechFailure.TextTooLong).Message, request.Target); return; }
        if (SpeechTextPolicy.SanitizedText(request.Text) is not { } text) { SetError("There is no text to read aloud.", request.Target); return; }
        if (ResolvedRequest(request, text) is not { } resolved) return;
        if (Settings.SelectedProvider != LearningSpeechProviderKind.System) { StartGeneratedAudio(text, resolved); return; }
        if (!CatalogIsLoaded) RefreshVoices();
        if (VoiceCatalog.Count == 0) { SetError(VoiceCatalogError ?? "No system speech voices are available.", request.Target); return; }
        var language = resolved.Language!.Value;
        if (SpeechVoicePolicy.Select(VoiceCatalog, language, Settings.VoiceId(language), _voiceComparer) is not { } voice)
        { SetError("No speech voice is available for this language.", request.Target); return; }
        var token = new SpeechPlaybackToken(); Telemetry.Begin(token, voice.Name, "system_speech", language);
        _activeToken = token; _activeRequest = resolved; ActiveTarget = resolved.Target; Phase = SpeechPlaybackPhase.Preparing; Changed();
        try { _provider.Speak(text, voice, Settings.RateMultiplier, token); }
        catch (Exception error)
        {
            if (_activeToken != token) return;
            Telemetry.Finish(token, ModelRequestOutcome.Failed, ErrorType(error)); ClearActivePlayback(); SetError(error.Message, resolved.Target);
        }
    }
    private SpeechRequest? ResolvedRequest(SpeechRequest request, string text)
    {
        var language = request.LanguageMode == SpeechLanguageMode.Fixed ? request.Language : SpeechLanguageResolver.Resolve(text, request.Language, _recognize, request.Context);
        if (language is null) { SetError(new SpeechException(SpeechFailure.UnrecognizedLanguage).Message, request.Target); return null; }
        return new(request.Target, text, language.Value, LanguageMode: SpeechLanguageMode.Fixed);
    }
    public void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language)
    {
        RequireOwner(); if (_exportToken is not null) throw new SpeechException(SpeechFailure.OperationInProgress);
        Stop(); var token = new SpeechPlaybackToken(); var target = new SpeechTarget(ownerId, "review.audio");
        _activeToken = token; ActiveTarget = target; _activeRequest = new(target, "", language, LanguageMode: SpeechLanguageMode.Fixed); Changed();
        Telemetry.Begin(token, "Review audio", "recorded_audio", language);
        try { _audioPlayer.Play(audio.CopyBytes(), 1, token); }
        catch { ClearActivePlayback(); Telemetry.Finish(token, ModelRequestOutcome.Failed); throw; }
    }
    internal bool IsPlaying(SpeechTarget target) => ActiveTarget == target;
    internal bool IsPreparing(SpeechTarget target) => ActiveTarget == target && Phase == SpeechPlaybackPhase.Preparing;
    internal string? ConfigurationError
    {
        get
        {
            try
            {
                switch (Settings.SelectedProvider)
                { case LearningSpeechProviderKind.Local: _ = Settings.LocalConfiguration(); break; case LearningSpeechProviderKind.Endpoint: _ = Settings.EndpointConfiguration(); break; }
                return null;
            }
            catch (Exception error) { return error.Message; }
        }
    }
    internal void RefreshLocalModels()
    {
        RequireOwner(); var models = ModelDownloads.InstalledModelOptions().Where(model => !SpeechModelPairingPolicy.IsKnownDecoder(model.Filename)).ToArray();
        if (!AvailableLocalModels.SequenceEqual(models)) { AvailableLocalModels = Array.AsReadOnly(models); Changed(); }
    }
    internal void SelectLocalModel(string path)
    {
        RequireOwner(); path = FoundationText.TrimWhitespaceAndNewlines(path);
        if (path.Length == 0) { Settings.SetLocalAssets("", ""); return; }
        var model = _files.Inspect(path); var filenames = SpeechModelPairingPolicy.DecoderFilenames(model.Filename);
        if (filenames.Count == 0) { Settings.SetLocalAssets(path, Settings.LocalProjectionPath); return; }
        var directories = new[] { Path.GetDirectoryName(model.Url.LocalPath)!, _modelDirectory };
        var decoder = directories.SelectMany(directory => filenames.Select(name => _files.Inspect(Path.Combine(directory, name))))
            .FirstOrDefault(file => file.Readable && file.Size is not null);
        Settings.SetLocalAssets(path, decoder?.Url.LocalPath ?? "");
    }
    private SpeechAudioSource? GeneratedSource(LearningLanguage language) => Settings.SelectedProvider switch
    {
        LearningSpeechProviderKind.System => null,
        LearningSpeechProviderKind.Local when LocalSpeechLanguage.Code(language) is null => throw new SpeechException(SpeechFailure.UnsupportedLanguage),
        LearningSpeechProviderKind.Local => new SpeechAudioSource.Local(Settings.LocalConfiguration()),
        LearningSpeechProviderKind.Endpoint => new SpeechAudioSource.Endpoint(Settings.EndpointConfiguration(), Settings.EndpointApiKey()),
        _ => throw new ArgumentOutOfRangeException(),
    };
    private void StartGeneratedAudio(string text, SpeechRequest request)
    {
        var language = request.Language!.Value;
        SpeechAudioSource source;
        try { if (GeneratedSource(language) is not { } configured) return; source = configured; }
        catch (Exception error) { SetError(error.Message, request.Target); return; }
        var token = new SpeechPlaybackToken();
        if (source is SpeechAudioSource.Local local) Telemetry.Begin(token, Path.GetFileName(local.Configuration.ModelUrl.LocalPath), "llama", language);
        else if (source is SpeechAudioSource.Endpoint endpoint) Telemetry.Begin(token, endpoint.Configuration.Endpoint.ModelName, "openai_compatible", language);
        double rate = Settings.RateMultiplier;
        _activeToken = token; ActiveTarget = request.Target; _activeRequest = request; Phase = SpeechPlaybackPhase.Preparing; Changed();
        var audioRequest = new SpeechAudioRequest(token, text, language, source, Settings.GenerationProfile.Sampling);
        bool isLocal = source is SpeechAudioSource.Local;
        if (isLocal) _pcmPlayer.Begin(SpeechLocalConfiguration.OutputSampleRate, rate, token);
        _audioTask = StartOwned(cancellation => GenerateAudioAsync(audioRequest, rate, isLocal, cancellation));
    }
    private async Task GenerateAudioAsync(SpeechAudioRequest request, double rate, bool isLocal, CancellationToken cancellation)
    {
        var token = request.Token; var target = _activeRequest?.Target;
        try
        {
            await _generator.GenerateAsync(request, AudioReceiver(new WeakReference<LearningSpeechController>(this), rate, token, cancellation), cancellation);
            cancellation.ThrowIfCancellationRequested(); if (_activeToken != token) return;
            Telemetry.GenerationCompleted(token);
            if (isLocal) await _pcmPlayer.EndInputAsync(token, cancellation);
        }
        catch (OperationCanceledException)
        {
            if (_activeToken != token) return;
            Telemetry.Finish(token, ModelRequestOutcome.Cancelled); _audioPlayer.Stop(); _pcmPlayer.Stop(); ClearActivePlayback();
        }
        catch (Exception error)
        {
            if (_activeToken != token) return;
            Telemetry.Finish(token, ModelRequestOutcome.Failed, ErrorType(error)); _audioPlayer.Stop(); _pcmPlayer.Stop(); ClearActivePlayback();
            if (target is not null) SetError(error.Message, target);
        }
    }
    private static Func<SpeechAudioChunk, Task> AudioReceiver(WeakReference<LearningSpeechController> weak, double rate, SpeechPlaybackToken token, CancellationToken cancellation) =>
        chunk => weak.TryGetTarget(out var owner)
            ? owner.OnOwnerAsync(() => owner.AcceptAudioAsync(chunk, rate, token, cancellation))
            : Task.FromCanceled(new CancellationToken(canceled: true));
    private async Task AcceptAudioAsync(SpeechAudioChunk chunk, double rate, SpeechPlaybackToken token, CancellationToken cancellation)
    {
        RequireOwner(); cancellation.ThrowIfCancellationRequested(); if (_activeToken != token) throw new OperationCanceledException();
        switch (chunk)
        {
            case SpeechAudioChunk.Encoded encoded: Telemetry.Generated(token, encoded.ByteCount); _audioPlayer.Play(encoded.CopyBytes(), rate, token); break;
            case SpeechAudioChunk.Pcm pcm: Telemetry.Generated(token, pcm.Value.ByteCount); await _pcmPlayer.EnqueueAsync(pcm.Value, token, cancellation); break;
        }
    }
    internal void Stop()
    {
        RequireOwner(); Cancel(_audioTask); _audioTask = null; _audioPlayer.Stop(); _pcmPlayer.Stop(); ClearError();
        if (_activeToken is not { } token) { ClearActivePlayback(); return; }
        Telemetry.Finish(token, ModelRequestOutcome.Cancelled); ClearActivePlayback(); _provider.Stop(token);
        StartOwned(_ => _generator.CancelAsync(token), reportFailure: true);
    }
    internal void Shutdown()
    {
        RequireOwner(); Stop(); _pcmPlayer.Shutdown(); StartOwned(_ => _generator.ShutdownAsync(), reportFailure: true);
    }
    public void Stop(Guid ownerId)
    {
        RequireOwner(); if (ActiveTarget?.OwnerId == ownerId) Stop(); if (ErrorTarget?.OwnerId == ownerId) ClearError();
    }
    internal void Stop(SpeechTarget target)
    {
        RequireOwner(); if (ActiveTarget == target) Stop(); if (ErrorTarget == target) ClearError();
    }
    internal string? Error(SpeechTarget target) => ErrorTarget == target ? PlaybackError : null;
    private void Receive(SpeechProviderEvent value)
    {
        RequireOwner(); if (_activeToken != value.Token) return;
        switch (value)
        {
            case SpeechProviderEvent.Prepared prepared: Telemetry.PlaybackPrepared(value.Token, prepared.Milliseconds); break;
            case SpeechProviderEvent.Started: Telemetry.PlaybackStarted(value.Token); Phase = SpeechPlaybackPhase.Playing; Changed(); break;
            case SpeechProviderEvent.Finished: Telemetry.Finish(value.Token, ModelRequestOutcome.Completed); ClearActivePlayback(); break;
            case SpeechProviderEvent.Cancelled: Telemetry.Finish(value.Token, ModelRequestOutcome.Cancelled); ClearActivePlayback(); break;
            case SpeechProviderEvent.Failed failed:
                Telemetry.Finish(value.Token, ModelRequestOutcome.Failed, "playback_failed"); var target = _activeRequest?.Target; ClearActivePlayback();
                Cancel(_audioTask); _audioPlayer.Stop(); _pcmPlayer.Stop(); StartOwned(_ => _generator.CancelAsync(value.Token), reportFailure: true);
                if (target is not null) SetError(failed.Message, target); break;
        }
    }
    private void SetError(string message, SpeechTarget target) { ErrorTarget = target; PlaybackError = message; Changed(); }
    private void ClearError() { ErrorTarget = null; PlaybackError = null; Changed(); }
    private void ClearActivePlayback() { _activeToken = null; _activeRequest = null; ActiveTarget = null; Phase = null; Changed(); }
    private static string ErrorType(Exception error) => error.GetType().FullName ?? error.GetType().Name;
    private void RequireOwner()
    { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Speech control must run on its owning UI context."); }
    private Task OnOwnerAsync(Func<Task> operation)
    {
        if (Environment.CurrentManagedThreadId == _ownerThread) return operation();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(async _ => { try { RequireOwner(); await operation(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }, null);
        return completion.Task;
    }
    private void Cancel(ControllerWork? work) { if (work?.Cancel() is { } error) _lifetimeErrors.Add(error); }
    private ControllerWork StartOwned(Func<CancellationToken, Task> operation, bool reportFailure = false)
    {
        var work = new ControllerWork(); var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(work); work.Task = ExecuteAsync(); _ = ObserveAsync(work.Task); admitted.SetResult(); return work;
        async Task ExecuteAsync()
        {
            await admitted.Task;
            try { await operation(work.Cancellation); }
            catch (Exception error) when (reportFailure) { _lifetimeErrors.Add(error); }
            finally { work.Complete(); _work.Remove(work); }
        }
    }
    internal async Task DrainAsync()
    {
        RequireOwner(); while (_work.Count > 0) await Task.WhenAll(_work.Select(work => ObserveAsync(work.Task)).ToArray());
        if (_lifetimeErrors.Count > 0) { var errors = _lifetimeErrors.ToArray(); _lifetimeErrors.Clear(); throw new AggregateException(errors); }
    }
    /// <summary>The host first cancels/joins external review exports, then releases this owner.
    /// Weak subscriptions already avoid rooting it, but explicit removal prevents notifications
    /// during dependency teardown. Player device drain remains its own owner's responsibility.</summary>
    public async ValueTask DisposeAsync()
    {
        RequireOwner();
        if (_exportToken is not null) throw new InvalidOperationException("Join the caller-owned speech export before disposing its controller.");
        try { Shutdown(); await DrainAsync(); }
        finally
        {
            Telemetry.PropertyChanged -= _relayChanged; Settings.PropertyChanged -= _relayChanged; Settings.PlaybackInvalidated -= _settingsInvalidation;
            _provider.EventHandler = null; _audioPlayer.EventHandler = null; _pcmPlayer.EventHandler = null;
            if (_modelDownloads is not null) { _modelDownloads.DirectoryChanged = null; _modelDownloads.ProtectedModelFilenames = () => new HashSet<string>(CanonicalStringComparer.Instance); }
        }
    }
    private static async Task ObserveAsync(Task task) { try { await task; } catch { /* The action's error path owns target/telemetry publication. */ } }
    private sealed class ControllerWork
    {
        private readonly CancellationTokenSource _cancellation = new(); private bool _complete;
        internal Task Task { get; set; } = Task.CompletedTask;
        internal CancellationToken Cancellation => _cancellation.Token;
        internal Exception? Cancel() { try { if (!_complete) _cancellation.Cancel(); return null; } catch (Exception error) { return error; } }
        internal void Complete() { _complete = true; _cancellation.Dispose(); }
    }
}
