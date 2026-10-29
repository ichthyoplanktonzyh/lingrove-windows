using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

public sealed partial class LearningSpeechControllerTests
{
    private static async Task ControllerFlowAsync(JsonElement rawRow)
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            string root = Path.Combine(Path.GetTempPath(), "lingrove-controller-flow-" + Guid.NewGuid()); Directory.CreateDirectory(Path.Combine(root, "app"));
            var row = JsonSerializer.SerializeToElement(ExpandFlow(JsonNode.Parse(rawRow.GetRawText()), root));
            foreach (string name in row.GetProperty("files").EnumerateArray().Select(value => value.GetString()!))
            { string file = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "GGUFfixture"); }
            var saved = new AppSettings(); foreach (var seed in row.GetProperty("seed").EnumerateObject())
                typeof(AppSettings).GetProperties().Single(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == seed.Name).SetValue(saved, seed.Value.Clone());
            var settings = new LearningSpeechSettings(() => saved, () => { }, new Credentials()); var provider = new FlowProvider(); var generator = new FlowGenerator(); var pcm = new FlowPCM(); var device = new FlowDevice();
            var modelManager = new FlowModels(root, row.GetProperty("files").EnumerateArray().Select(value => value.GetString()!));
            int modelCreations = 0; var scores = new Dictionary<string, IReadOnlyDictionary<string, double>>(); var logs = new JsonArray();
            var telemetry = new SpeechTelemetry((context, phase, details) =>
            {
                var data = details.ToDictionary(pair => pair.Key, pair => pair.Value);
                if (data.TryGetValue("error_type", out string? name)) data["error_type"] = name.EndsWith("FlowProviderException", StringComparison.Ordinal) ? "provider_error" : name.EndsWith("SpeechException", StringComparison.Ordinal) || name.EndsWith("SpeechExportException", StringComparison.Ordinal) ? "speech_error" : name;
                logs.Add(new JsonObject { ["model"] = context.ModelName, ["engine"] = context.Engine, ["phase"] = phase, ["details"] = JsonSerializer.SerializeToNode(data) });
            }, now: () => 10);
            var controller = new LearningSpeechController(settings, telemetry, provider, generator, device, pcm, new FlowSystemExporter(), device,
                text => scores.GetValueOrDefault(text) ?? new Dictionary<string, double>(), StringComparer.Ordinal, Path.Combine(root, "app"), () => { modelCreations++; return modelManager; });
            var owners = new Dictionary<string, Guid>(); var ownerNames = new Dictionary<Guid, string>(); var tokens = new List<SpeechPlaybackToken>();
            var exports = new Dictionary<string, SpeechExportRequest>(); var exportJobs = new Dictionary<string, Task<SpeechExportAudio>>(); var exportCancellations = new Dictionary<string, CancellationTokenSource>(); var localFingerprints = new Dictionary<string, string>();
            Guid Owner(string name) { if (!owners.TryGetValue(name, out var value)) { value = Guid.Parse($"00000000-0000-0000-0000-{owners.Count + 1:x12}"); owners.Add(name, value); ownerNames.Add(value, name); } return value; }
            SpeechTarget Target(JsonElement action) => new(Owner(Text(action, "owner", "a")), Text(action, "item", "translation"));
            JsonNode? TargetValue(SpeechTarget? target) => target is null ? null : new JsonObject { ["owner"] = ownerNames[target.OwnerId], ["item"] = target.ItemId };
            string TokenName(SpeechPlaybackToken token) { int index = tokens.IndexOf(token); if (index < 0) { index = tokens.Count; tokens.Add(token); } return $"token-{index + 1}"; }
            JsonObject SourceValue(SpeechAudioSource value) => value switch
            {
                SpeechAudioSource.Local local => new() { ["kind"] = "local", ["model"] = local.Configuration.ModelUrl.LocalPath, ["projection"] = local.Configuration.ProjectionUrl.LocalPath, ["reference"] = local.Configuration.ReferenceAudioUrl?.LocalPath },
                SpeechAudioSource.Endpoint endpoint => new() { ["kind"] = "endpoint", ["baseURL"] = endpoint.Configuration.Endpoint.BaseUrl.AbsoluteUri, ["model"] = endpoint.Configuration.Endpoint.ModelName, ["voice"] = endpoint.Configuration.Voice, ["key"] = endpoint.ApiKey }, _ => throw new InvalidOperationException(),
            };
            JsonObject RequestValue(SpeechAudioRequest value) => new() { ["token"] = TokenName(value.Token), ["text"] = value.Text, ["language"] = value.Language.Code(), ["source"] = SourceValue(value.Source), ["sampling"] = SamplingValue(value.Sampling) };
            JsonObject ExportValue(string name, SpeechExportRequest request)
            {
                string fingerprint = request.Fingerprint; JsonNode source;
                if (request.AudioSource is SpeechExportRequest.Source.System system) source = new JsonObject { ["kind"] = "system", ["voice"] = system.VoiceId };
                else
                {
                    var generated = (SpeechExportRequest.Source.Generated)request.AudioSource;
                    source = new JsonObject { ["kind"] = "generated", ["audio"] = SourceValue(generated.Audio), ["sampling"] = SamplingValue(generated.Sampling) };
                    if (generated.Audio is SpeechAudioSource.Local)
                    { if (!localFingerprints.ContainsKey(fingerprint)) localFingerprints.Add(fingerprint, $"local-fingerprint-{localFingerprints.Count + 1}"); fingerprint = localFingerprints[fingerprint]; }
                }
                return new() { ["name"] = name, ["token"] = TokenName(request.Token), ["text"] = request.Text, ["language"] = request.Language.Code(), ["fingerprint"] = fingerprint, ["source"] = source };
            }
            JsonNode Observe(JsonNode? result = null)
            {
                var activeToken = Field<SpeechPlaybackToken?>(controller, "_activeToken"); if (activeToken is { } active) TokenName(active);
                foreach (var started in provider.Starts) TokenName(started.Token);
                foreach (string name in exports.Keys.Order(StringComparer.Ordinal)) TokenName(exports[name].Token);
                foreach (var request in generator.Requests) TokenName(request.Token);
                var activeRequest = Field<SpeechRequest?>(controller, "_activeRequest");
                var value = new JsonObject
                {
                    ["state"] = new JsonObject { ["active"] = TargetValue(controller.ActiveTarget), ["errorTarget"] = TargetValue(controller.ErrorTarget), ["error"] = controller.PlaybackError, ["phase"] = controller.Phase?.ToString().ToLowerInvariant(),
                        ["catalogLoaded"] = controller.CatalogIsLoaded, ["catalogError"] = controller.VoiceCatalogError, ["voices"] = new JsonArray(controller.VoiceCatalog.Select(VoiceValue).ToArray()),
                        ["activeRequest"] = activeRequest is null ? null : new JsonObject { ["target"] = TargetValue(activeRequest.Target), ["text"] = activeRequest.Text, ["language"] = activeRequest.Language?.Code(), ["context"] = activeRequest.Context, ["mode"] = activeRequest.LanguageMode == SpeechLanguageMode.Fixed ? "fixed" : "automatic" },
                        ["exporting"] = Field<SpeechPlaybackToken?>(controller, "_exportToken") is not null, ["exportRevision"] = Field<ulong>(controller, "_exportRevision").ToString(CultureInfo.InvariantCulture) },
                    ["provider"] = new JsonObject { ["refreshes"] = provider.RefreshCount, ["starts"] = new JsonArray(provider.Starts.Select(started => (JsonNode)new JsonObject { ["text"] = started.Text, ["voice"] = started.Voice.Id, ["rate"] = started.Rate, ["token"] = TokenName(started.Token) }).ToArray()), ["stopped"] = JsonSerializer.SerializeToNode(provider.Stopped.Select(TokenName)) },
                    ["generation"] = new JsonObject { ["requests"] = new JsonArray(generator.Requests.Select(request => (JsonNode)RequestValue(request)).ToArray()), ["cancelled"] = generator.CancelledCount },
                    ["pcm"] = new JsonObject { ["ended"] = pcm.Ended, ["chunks"] = pcm.Chunks }, ["encoded"] = new JsonArray(device.Players.Select(player => (JsonNode)player.Value()).ToArray()),
                    ["models"] = new JsonObject { ["created"] = modelCreations, ["available"] = new JsonArray(controller.AvailableLocalModels.Select(model => (JsonNode)new JsonObject { ["filename"] = model.Filename, ["path"] = model.Url.LocalPath, ["displayName"] = model.DisplayName }).ToArray()), ["protected"] = modelCreations == 0 ? null : JsonSerializer.SerializeToNode(modelManager.ProtectedModelFilenames().Order(CanonicalStringComparer.Instance)) },
                    ["exports"] = new JsonArray(exports.Keys.Order(StringComparer.Ordinal).Select(name => (JsonNode)ExportValue(name, exports[name])).ToArray()), ["logs"] = logs.DeepClone(), ["result"] = result,
                };
                return NormalizeFlow(value, root)!;
            }
            var trace = new JsonArray { Observe() };
            try
            {
                foreach (var action in row.GetProperty("actions").EnumerateArray())
                {
                    JsonNode? result = null; var previous = Field<object?>(controller, "_audioTask"); var previousToken = Field<SpeechPlaybackToken?>(controller, "_activeToken");
                    Task? PreviousTask() => previous?.GetType().GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(previous) as Task;
                    try
                    {
                        switch (Text(action, "op"))
                        {
                            case "toggle":
                                int before = generator.Requests.Count; controller.Toggle(new(Target(action), Text(action, "text", "Hello."), Language(action), action.TryGetProperty("context", out var context) ? context.GetString() : null, Text(action, "mode") == "fixed" ? SpeechLanguageMode.Fixed : SpeechLanguageMode.Automatic));
                                if (settings.SelectedProvider != LearningSpeechProviderKind.System && controller.ActiveTarget is not null && Field<SpeechPlaybackToken?>(controller, "_activeToken") != previousToken) await WaitFlow(() => generator.Requests.Count > before || controller.ActiveTarget is null, deadline); break;
                            case "stop": controller.Stop(); break;
                            case "stopOwner": controller.Stop(Owner(Text(action, "owner", "a"))); break;
                            case "stopTarget": controller.Stop(Target(action)); break;
                            case "refreshVoices": controller.RefreshVoices(); break;
                            case "catalog": provider.Voices = action.GetProperty("voices").EnumerateArray().Select(Voice).ToArray(); break;
                            case "providerFailure": provider.RefreshError = Flag(action, "refresh"); provider.SpeakError = Flag(action, "speak"); provider.CancelOnStop = Flag(action, "cancelOnStop"); break;
                            case "recognition": scores = action.GetProperty("scores").EnumerateObject().ToDictionary(value => value.Name, value => (IReadOnlyDictionary<string, double>)value.Value.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble())); break;
                            case "event":
                                var token = tokens[Number(action, "index")]; SpeechProviderEvent value = Text(action, "kind") switch
                                { "prepared" => new SpeechProviderEvent.Prepared(token, Number(action, "milliseconds", 3)), "started" => new SpeechProviderEvent.Started(token), "finished" => new SpeechProviderEvent.Finished(token), "cancelled" => new SpeechProviderEvent.Cancelled(token), "failed" => new SpeechProviderEvent.Failed(token, Text(action, "message", "fixture playback failed")), _ => throw new InvalidOperationException() };
                                provider.EventHandler?.Invoke(value); break;
                            case "emitPCM": await generator.EmitPCMAsync(tokens[Number(action, "index")]); break;
                            case "finishGeneration": generator.Finish(tokens[Number(action, "index")]); if (ControllerTask(controller) is { } done) await done; break;
                            case "generationFailure": generator.Fail(tokens[Number(action, "index")]); if (ControllerTask(controller) is { } failed) await failed; break;
                            case "invalidEncoded": device.FailInit = true; await generator.CompleteInvalidAsync(tokens[Number(action, "index")]); if (ControllerTask(controller) is { } invalid) await invalid; device.FailInit = false; break;
                            case "encodedPlan": device.Seconds = action.TryGetProperty("duration", out var seconds) ? Scalar(seconds) : 1; device.FailInit = Flag(action, "failInit"); device.PlaySucceeds = !action.TryGetProperty("play", out var play) || play.GetBoolean(); break;
                            case "audition": controller.Audition(new SpeechExportAudio(new byte[Number(action, "bytes", 8)], "wav"), Owner(Text(action, "owner", "a")), Language(action)); break;
                            case "configuration": result = JsonValue.Create(controller.ConfigurationError); break;
                            case "refreshModels": controller.RefreshLocalModels(); break;
                            case "selectModel": controller.SelectLocalModel(Text(action, "path")); break;
                            case "runtime": telemetry.Receive(new(new() { SessionId = Guid.Parse("00000000-0000-0000-0000-000000000999"), Generation = (ulong)Number(action, "generation", 1), Phase = JsonSerializer.Deserialize<SpeechLocalRuntimePhase>(JsonSerializer.Serialize(Text(action, "phase", "loading"))), ProcessId = action.TryGetProperty("pid", out var pid) ? pid.GetInt32() : null, LoadedModelFilenames = action.GetProperty("files").EnumerateArray().Select(name => name.GetString()!).ToHashSet() })); break;
                            case "exportRequest": exports[Text(action, "name", "x")] = controller.ExportRequest(Text(action, "text", "Hello."), Language(action), action.TryGetProperty("context", out var exportContext) ? exportContext.GetString() : null); break;
                            case "exportStart":
                                string name = Text(action, "name", "x"); var request = exports[name]; bool entered = false; var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline); exportCancellations.Add(name, cancellation);
                                var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                                async Task<SpeechExportAudio> ExportAsync() { await admitted.Task; entered = true; return await controller.ExportAudioAsync(request, cancellation.Token); }
                                exportJobs.Add(name, ExportAsync()); admitted.SetResult(); await WaitFlow(() => entered, deadline);
                                if (request.AudioSource is SpeechExportRequest.Source.System) { try { await exportJobs[name]; } catch { /* The later join observes the outcome. */ } }
                                if (Field<SpeechPlaybackToken?>(controller, "_exportToken") == request.Token && request.AudioSource is SpeechExportRequest.Source.Generated) await WaitFlow(() => generator.Requests.Any(value => value.Token == request.Token), deadline); break;
                            case "exportPCM": await generator.EmitPCMAsync(exports[Text(action, "name", "x")].Token); break;
                            case "exportFinish": case "exportWave":
                                string job = Text(action, "name", "x"); if (Text(action, "op") == "exportWave") await generator.CompleteWaveAsync(exports[job].Token); else generator.Finish(exports[job].Token);
                                result = AudioValue(await exportJobs[job]); break;
                            case "exportJoin": result = AudioValue(await exportJobs[Text(action, "name", "x")]); break;
                            case "exportCancel": string cancelled = Text(action, "name", "x"); exportCancellations[cancelled].Cancel(); try { await exportJobs[cancelled]; } catch { /* Cancellation is observed separately by join. */ } break;
                            default: Apply(action, settings); break;
                        }
                    }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { result = new JsonObject { ["error"] = "cancelled" }; }
                    catch (Exception error) when (error is SpeechException or SpeechExportException or FlowProviderException) { result = new JsonObject { ["error"] = error.Message }; }
                    if (previousToken != Field<SpeechPlaybackToken?>(controller, "_activeToken") && PreviousTask() is { } previousTask) await previousTask;
                    trace.Add(Observe(result));
                }
                var expected = JsonNode.Parse(rawRow.GetProperty("expected").GetRawText());
                Assert.True(JsonNode.DeepEquals(expected, trace), Text(row, "id") + "\n" + string.Join("\n", AnkiReviewSessionTests.Differences(expected, trace).Take(25)));
            }
            finally
            {
                controller.Shutdown(); await generator.ShutdownAsync(); foreach (var cancellation in exportCancellations.Values) cancellation.Cancel();
                foreach (var task in exportJobs.Values) { try { await task; } catch { /* Join every caller-owned export before releasing the controller. */ } }
                await controller.DisposeAsync(); foreach (var cancellation in exportCancellations.Values) cancellation.Dispose(); Directory.Delete(root, true);
            }
        });
    }
    private static Task? ControllerTask(LearningSpeechController controller)
    { var work = Field<object?>(controller, "_audioTask"); return work?.GetType().GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work) as Task; }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static int Number(JsonElement row, string key, int fallback = 0) => row.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
    private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.GetBoolean();
    private static LearningLanguage Language(JsonElement row) => LearningLanguages.FromCode(Text(row, "language", "en"))!.Value;
    private static async Task WaitFlow(Func<bool> predicate, CancellationToken deadline)
    { while (!predicate()) await Task.Delay(1, deadline); }
    private static JsonNode SamplingValue(LearningModelSamplingOverrides value) => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })!;
    private static SpeechVoice Voice(JsonElement row) => new(Text(row, "id"), Text(row, "name", Text(row, "id")), Text(row, "language", "en-US"), (SpeechVoiceQuality)Number(row, "quality"), Flag(row, "preferred"), Flag(row, "novelty"));
    private static JsonNode VoiceValue(SpeechVoice value) => new JsonObject { ["id"] = value.Id, ["name"] = value.Name, ["language"] = value.Language, ["quality"] = (int)value.Quality, ["preferred"] = value.IsSystemPreferred, ["novelty"] = value.IsNovelty };
    private static JsonObject AudioValue(SpeechExportAudio value) => new() { ["bytes"] = value.ByteCount, ["digest"] = SpeechExportPolicy.Digest(value.CopyBytes()), ["extension"] = value.FileExtension };
    private static JsonNode? ExpandFlow(JsonNode? value, string root)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out var s)) return JsonValue.Create(s.Replace("$ASSETS", root.Replace('\\', '/'), StringComparison.Ordinal));
        if (value is JsonArray array) return new JsonArray(array.Select(item => ExpandFlow(item, root)).ToArray());
        if (value is JsonObject obj) return new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, ExpandFlow(pair.Value, root)))); return value?.DeepClone();
    }
    private static JsonNode? NormalizeFlow(JsonNode? value, string root)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out var s) && (s.StartsWith(root, StringComparison.Ordinal) || s.StartsWith(root.Replace('\\', '/'), StringComparison.Ordinal)))
        { string suffix = s[root.Length..]; return JsonValue.Create("$ASSETS" + suffix.Replace('\\', '/')); }
        if (value is JsonArray array) return new JsonArray(array.Select(item => NormalizeFlow(item, root)).ToArray());
        if (value is JsonObject obj) return new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, NormalizeFlow(pair.Value, root)))); return value?.DeepClone();
    }
    private sealed class FlowProviderException() : Exception("Fake speech provider failed.");
    private sealed class FlowProvider : ISpeechProviding
    {
        internal sealed record Start(string Text, SpeechVoice Voice, double Rate, SpeechPlaybackToken Token);
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        internal IReadOnlyList<SpeechVoice> Voices { get; set; } = [new("en-US", "English", "en-US", SpeechVoiceQuality.Standard), new("es-ES", "Spanish", "es-ES", SpeechVoiceQuality.Enhanced)];
        internal bool RefreshError, SpeakError, CancelOnStop; internal int RefreshCount;
        internal List<Start> Starts { get; } = []; internal List<SpeechPlaybackToken> Stopped { get; } = [];
        public IReadOnlyList<SpeechVoice> RefreshVoices() { RefreshCount++; if (RefreshError) throw new FlowProviderException(); return Voices; }
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) { if (SpeakError) throw new FlowProviderException(); Starts.Add(new(text, voice, rateMultiplier, token)); }
        public void Stop(SpeechPlaybackToken token) { Stopped.Add(token); if (CancelOnStop) EventHandler?.Invoke(new SpeechProviderEvent.Cancelled(token)); }
    }
    private sealed class FlowPCM : ISpeechPCMPlaying
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; } internal bool Ended; internal int Chunks, Stops;
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => Ended = false;
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) { Chunks++; EventHandler?.Invoke(new SpeechProviderEvent.Started(token)); return Task.CompletedTask; }
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) { Ended = true; return Task.CompletedTask; }
        public void Stop() { Stops++; } public void Shutdown() { }
    }
    private sealed class FlowGenerator : ISpeechAudioGenerating
    {
        private readonly object _gate = new(); private readonly Dictionary<SpeechPlaybackToken, TaskCompletionSource> _waiters = [];
        private readonly Dictionary<SpeechPlaybackToken, Func<SpeechAudioChunk, Task>> _receivers = []; private int _cancelled;
        internal List<SpeechAudioRequest> Requests { get; } = [];
        internal Action? CancellationCallback { get; set; }
        internal int CancelledCount { get { lock (_gate) return _cancelled; } }
        public async Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken)
        {
            _receivers[request.Token] = receive; cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); lock (_gate) _waiters.Add(request.Token, waiter);
            using var registration = cancellationToken.Register(() => Cancel(request.Token));
            using var callback = cancellationToken.Register(() => CancellationCallback?.Invoke()); await waiter.Task;
        }
        private void Cancel(SpeechPlaybackToken token) { TaskCompletionSource? waiter; lock (_gate) { if (!_waiters.Remove(token, out waiter)) return; _cancelled++; } waiter.TrySetCanceled(); }
        public Task CancelAsync(SpeechPlaybackToken token) { Cancel(token); return Task.CompletedTask; }
        public Task ShutdownAsync() { SpeechPlaybackToken[] tokens; lock (_gate) tokens = _waiters.Keys.ToArray(); foreach (var token in tokens) Cancel(token); return Task.CompletedTask; }
        internal Task EmitPCMAsync(SpeechPlaybackToken token) => _receivers[token](new SpeechAudioChunk.Pcm(new(new byte[9600], 24000)));
        internal void Finish(SpeechPlaybackToken token) { TaskCompletionSource? waiter; lock (_gate) _waiters.Remove(token, out waiter); waiter?.TrySetResult(); }
        internal void Fail(SpeechPlaybackToken token) { TaskCompletionSource? waiter; lock (_gate) _waiters.Remove(token, out waiter); waiter?.TrySetException(new SpeechException(SpeechFailure.GenerationFailed)); }
        private async Task CompleteAsync(SpeechPlaybackToken token, SpeechAudioChunk chunk)
        {
            try { await _receivers[token](chunk); Finish(token); }
            catch (Exception error) { TaskCompletionSource? waiter; lock (_gate) _waiters.Remove(token, out waiter); waiter?.TrySetException(error); }
        }
        internal Task CompleteInvalidAsync(SpeechPlaybackToken token) => CompleteAsync(token, new SpeechAudioChunk.Encoded(new byte[] { 1, 2, 3 }));
        internal Task CompleteWaveAsync(SpeechPlaybackToken token) => CompleteAsync(token, new SpeechAudioChunk.Encoded(SpeechExportPolicy.Wav(new byte[9600], 24000).CopyBytes()));
    }
    private sealed class FlowDevice : ISpeechAudioPlaying, ISpeechAudioDuration
    {
        internal sealed class Player(byte[] bytes, double seconds)
        {
            internal double Rate = 1; internal bool EnableRate, Stopped;
            internal JsonObject Value() => new() { ["bytes"] = bytes.Length, ["digest"] = SpeechExportPolicy.Digest(bytes), ["duration"] = double.IsFinite(seconds) ? JsonValue.Create(seconds) : JsonValue.Create(double.IsNaN(seconds) ? "nan" : seconds > 0 ? "infinity" : "-infinity"), ["rate"] = Rate, ["enableRate"] = EnableRate, ["stopped"] = Stopped };
        }
        internal List<Player> Players { get; } = []; internal double Seconds = 1; internal bool FailInit, PlaySucceeds = true; private Player? _active;
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        private Player Create(ReadOnlySpan<byte> bytes) { if (FailInit) throw new SpeechException(SpeechFailure.InvalidAudio); var player = new Player(bytes.ToArray(), Seconds); Players.Add(player); return player; }
        public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token)
        {
            Stop(); var player = Create(data); if (!(Seconds > 0 && Seconds <= 90 && double.IsFinite(Seconds))) throw new SpeechException(SpeechFailure.InvalidAudio);
            _active = player; player.EnableRate = true; player.Rate = (double)(float)rate;
            if (!PlaySucceeds) { Stop(); throw new SpeechException(SpeechFailure.InvalidAudio); } EventHandler?.Invoke(new SpeechProviderEvent.Started(token));
        }
        public void Stop() { if (_active is not null) _active.Stopped = true; _active = null; }
        public double Duration(ReadOnlySpan<byte> audio) { Create(audio); return Seconds; }
    }
    private sealed class FlowSystemExporter : ISystemSpeechAudioExporting
    {
        public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(SpeechExportPolicy.Wav(new byte[9600], 24000)); }
    }
    private sealed class FlowModels(string root, IEnumerable<string> filenames) : ISpeechModelManagement
    {
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        // These explicit inventory inputs belong to the fake platform boundary. This is not a
        // Windows model-discovery implementation; real transfer/discovery remains unactivated.
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => filenames.Where(name => name.StartsWith("app/", StringComparison.Ordinal) && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            .Select(name => new RuntimeModelOption(Path.GetFileName(name), new Uri(Path.GetFullPath(Path.Combine(root, name))))).OrderBy(model => model.DisplayName, CanonicalStringComparer.Instance).ToArray();
    }
}
