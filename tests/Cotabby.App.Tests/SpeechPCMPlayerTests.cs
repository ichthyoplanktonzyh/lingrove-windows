using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Speech;
using Cotabby.Core.Speech;

namespace Cotabby.App.Tests;

/// <summary>The complete original facade's traces drive the actual C# facade over a preparation
/// gate and captured buffer callbacks. A real serial UI-like context owns each test, including
/// cleanup. Native device work is deliberately absent; this verifies orchestration and identity.</summary>
public sealed class SpeechPCMPlayerTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-player-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Text(Corpus, "macReference"));
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(33, rows.Length);
        Assert.Equal("c2f537e09aad4403ed04f2a6b26a31b0953519378c5d7a4195aaf402de23d481", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { Text(r, "id") });
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalFacade(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id);
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new GatedRenderer(); var player = new SpeechPCMPlayer(renderer);
            var tokens = new Dictionary<string, SpeechPlaybackToken>(); var names = new Dictionary<SpeechPlaybackToken, string>();
            var events = new JsonArray(); var tasks = new Dictionary<string, Task>(); var cancellations = new List<CancellationTokenSource>();
            var taskCancellations = new Dictionary<string, CancellationTokenSource>(); var taskPreparations = new Dictionary<string, int>(); var preparations = new List<Task>();
            SpeechPlaybackToken Token(string name)
            {
                if (!tokens.TryGetValue(name, out var value)) { value = new(); tokens.Add(name, value); names.Add(value, name); } return value;
            }
            player.EventHandler = e => events.Add(Event(e, names[e.Token]));
            JsonObject Snapshot(string? result = null)
            {
                var ledger = Field<SpeechPCMPlaybackState>(player, "_state"); var token = Field<SpeechPlaybackToken?>(player, "_token");
                return new() { ["player"] = new JsonObject
                {
                    ["token"] = token is { } t ? names[t] : null, ["revision"] = Field<ulong>(player, "_revision").ToString(CultureInfo.InvariantCulture), ["rate"] = Field<double>(player, "_rate"),
                    ["sampleRate"] = Field<int?>(player, "_sampleRate"), ["playing"] = Field<bool>(player, "_playing"), ["queued"] = ledger.QueuedSamples, ["total"] = ledger.TotalSamples,
                    ["ended"] = ledger.InputEnded, ["preparation"] = Field<object?>(player, "_preparation") is not null, ["watchdog"] = Field<object?>(player, "_drainWatchdog") is not null,
                }, ["renderer"] = renderer.Snapshot(), ["events"] = events.DeepClone(), ["result"] = result };
            }
            var trace = new JsonArray { Snapshot() };
            try
            {
                foreach (var action in row.GetProperty("actions").EnumerateArray())
                {
                    string? result = null;
                    try
                    {
                        var token = Token(Text(action, "token", "a"));
                        switch (Text(action, "op"))
                        {
                            case "begin":
                                player.Begin(Number(action, "rateHz", 24000), action.TryGetProperty("rate", out var rate) ? rate.GetDouble() : 1, token);
                                var work = Field<object>(player, "_preparation"); preparations.Add((Task)work.GetType().GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work)!);
                                await WaitAsync(() => renderer.Preparations.Count == preparations.Count, deadline); break;
                            case "release": case "fail":
                                int index = Number(action, "index"); renderer.Release(index, Text(action, "op") == "fail");
                                try { await preparations[index]; }
                                finally { foreach (var pair in tasks.Where(pair => taskPreparations[pair.Key] == index)) { try { await pair.Value; } catch { /* Readiness settles all waiters at this observation barrier. */ } } }
                                break;
                            case "enqueue": case "launch":
                                var chunk = new SpeechPCMChunk(new byte[Number(action, "samples", 19200) * 4], Number(action, "rateHz", 24000));
                                if (Text(action, "op") == "launch")
                                {
                                    bool entered = false; string taskName = Text(action, "task", "job"); var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline);
                                    cancellations.Add(cancellation); taskCancellations.Add(taskName, cancellation); taskPreparations.Add(taskName, preparations.Count - 1);
                                    var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                                    async Task EnqueueAsync() { await admitted.Task; entered = true; await player.EnqueueAsync(chunk, token, cancellation.Token); }
                                    tasks.Add(taskName, EnqueueAsync()); admitted.SetResult(); await WaitAsync(() => entered, deadline);
                                }
                                else if (Flag(action, "cancel"))
                                {
                                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await player.EnqueueAsync(chunk, token, cancelled.Token);
                                }
                                else await player.EnqueueAsync(chunk, token, deadline);
                                break;
                            case "join": await tasks[Text(action, "task", "job")]; break;
                            case "cancel": taskCancellations[Text(action, "task", "job")].Cancel(); break;
                            case "end": await player.EndInputAsync(token, deadline); break;
                            case "played": await renderer.Preparations[Number(action, "index")].Played(Number(action, "buffer")); break;
                            case "invalidate": await renderer.Preparations[Number(action, "index")].Invalidated(); break;
                            case "stop": player.Stop(); break;
                            case "shutdown": player.Shutdown(); break;
                            case "waitFailure": await WaitAsync(() => events.OfType<JsonObject>().Any(e => e["kind"]!.GetValue<string>() == "failed" && e["token"]!.GetValue<string>() == Text(action, "token", "a")), deadline); break;
                            default: throw new InvalidOperationException("Unknown fixture action");
                        }
                    }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { result = "cancelled"; }
                    catch (SpeechException error) when (error.Failure == SpeechFailure.InvalidAudio) { result = "invalidAudio"; }
                    trace.Add(Snapshot(result));
                }
                var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
                Assert.True(JsonNode.DeepEquals(expected, trace), $"{id}\nexpected={expected}\nactual={trace}");
            }
            finally
            {
                player.Shutdown(); foreach (var cancellation in cancellations) cancellation.Cancel(); renderer.ReleaseAll();
                foreach (var task in preparations.Concat(tasks.Values)) { try { await task; } catch { /* A retired operation still joins before the context closes. */ } }
                await player.DrainAsync(); foreach (var cancellation in cancellations) cancellation.Dispose();
            }
        });
    }

    [Fact]
    public async Task DeviceCallbacksHopToOwnerAndShutdownJoinsHeldPreparation()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new GatedRenderer(); var player = new SpeechPCMPlayer(renderer, failure =>
            { Assert.Equal(SpeechFailure.InvalidAudio, failure); return "Localized invalid audio"; });
            var events = new List<SpeechProviderEvent>(); int owner = Environment.CurrentManagedThreadId;
            player.EventHandler = e => { Assert.Equal(owner, Environment.CurrentManagedThreadId); events.Add(e); };
            var token = new SpeechPlaybackToken(); player.Begin(24000, 1, token);
            try
            {
                await WaitAsync(() => renderer.Preparations.Count == 1, deadline);
                await Task.Run(() => renderer.Preparations[0].Invalidated(), deadline);
                Assert.Equal("Localized invalid audio", Assert.IsType<SpeechProviderEvent.Failed>(Assert.Single(events)).Message);
                player.Shutdown(); var drain = player.DrainAsync(); Assert.False(drain.IsCompleted);
                renderer.ReleaseAll(); await drain;
                Assert.Contains(renderer.Stops, stop => stop.Release); Assert.Empty(renderer.Enqueues);
            }
            finally { player.Shutdown(); renderer.ReleaseAll(); await player.DrainAsync(); }
        });
    }
    [Fact]
    public async Task RendererRetirementUsesIncreasingRevisionsAndReportsContractViolationAtDrain()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new GatedRenderer(); var player = new SpeechPCMPlayer(renderer);
            player.Begin(24000, 1, new());
            await WaitAsync(() => renderer.Preparations.Count == 1, deadline); renderer.Release(0);
            player.Stop(); player.Begin(24000, 1, new());
            await WaitAsync(() => renderer.Preparations.Count == 2, deadline); renderer.Release(1);
            renderer.StopError = new InvalidOperationException("scripted retirement failure"); player.Shutdown();
            var error = await Assert.ThrowsAsync<AggregateException>(() => player.DrainAsync()); Assert.Single(error.InnerExceptions);
            Assert.Equal(new ulong[] { 1, 3, 4, 6 }, renderer.Stops.Select(stop => stop.Revision));
            Assert.Equal(new ulong[] { 2, 5 }, renderer.Preparations.Select(preparation => preparation.Revision));
            renderer.StopError = null; await player.DrainAsync();
        });
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task SuspendedDeviceWorkCannotPublishForRetiredToken(bool holdPlay)
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renderer = new GatedRenderer { EnqueueGate = holdPlay ? null : gate, PlayGate = holdPlay ? gate : null };
            var player = new SpeechPCMPlayer(renderer); var events = new List<SpeechProviderEvent>(); player.EventHandler = events.Add;
            var old = new SpeechPlaybackToken(); var current = new SpeechPlaybackToken(); Task? enqueue = null;
            player.Begin(24000, 1, old);
            try
            {
                await WaitAsync(() => renderer.Preparations.Count == 1, deadline); renderer.Release(0);
                enqueue = player.EnqueueAsync(new SpeechPCMChunk(new byte[76800], 24000), old, deadline);
                await WaitAsync(() => holdPlay ? renderer.PlayEntered : renderer.Enqueues.Count == 1, deadline);
                player.Stop(); player.Begin(24000, 1, current);
                await WaitAsync(() => renderer.Preparations.Count == 2, deadline); renderer.Release(1); gate.SetResult();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enqueue);
                Assert.DoesNotContain(events, e => e is SpeechProviderEvent.Started && e.Token == old);
                await player.EnqueueAsync(new SpeechPCMChunk(new byte[76800], 24000), current, deadline); await player.EndInputAsync(current, deadline);
                await renderer.Preparations[1].Played(0);
                Assert.Single(events.OfType<SpeechProviderEvent.Finished>()); Assert.Equal(current, events.OfType<SpeechProviderEvent.Finished>().Single().Token);
            }
            finally
            {
                player.Shutdown(); renderer.ReleaseAll(); gate.TrySetResult();
                if (enqueue is not null) { try { await enqueue; } catch (OperationCanceledException) { } }
                await player.DrainAsync();
            }
        });
    }
    [Fact]
    public async Task ThrowingCancellationCallbackCannotInterruptRetirement()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var renderer = new GatedRenderer { CancellationCallback = () => throw new InvalidOperationException("scripted cancellation callback") };
            var player = new SpeechPCMPlayer(renderer); player.Begin(24000, 1, new());
            await WaitAsync(() => renderer.Preparations.Count == 1, deadline);
            player.Stop();
            Assert.Null(Field<SpeechPlaybackToken?>(player, "_token")); Assert.Null(Field<object?>(player, "_preparation"));
            Assert.Null(Field<int?>(player, "_sampleRate")); Assert.False(Field<bool>(player, "_playing"));
            player.Shutdown(); renderer.ReleaseAll();
            var error = await Assert.ThrowsAsync<AggregateException>(() => player.DrainAsync()); Assert.Single(error.InnerExceptions);
            Assert.Contains(renderer.Stops, stop => stop.Release); await player.DrainAsync();
        });
    }
    [Fact]
    public async Task OwnerContextIsRequiredAndOffThreadCallsAreRejected()
    {
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(null); Assert.Throws<InvalidOperationException>(() => new SpeechPCMPlayer(new GatedRenderer())); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        using var ui = new BatchUiContext();
        await ui.RunAsync(async _ =>
        {
            var player = new SpeechPCMPlayer(new GatedRenderer());
            await Task.Run(() => Assert.Throws<InvalidOperationException>(() => player.Begin(24000, 1, new())));
            player.Shutdown(); await player.DrainAsync();
        });
    }
    private static JsonObject Event(SpeechProviderEvent e, string name)
    {
        var value = new JsonObject { ["token"] = name };
        switch (e)
        {
            case SpeechProviderEvent.Prepared: value["kind"] = "prepared"; value["milliseconds"] = "measured"; break;
            case SpeechProviderEvent.Started: value["kind"] = "started"; break;
            case SpeechProviderEvent.Finished: value["kind"] = "finished"; break;
            case SpeechProviderEvent.Cancelled: value["kind"] = "cancelled"; break;
            case SpeechProviderEvent.Failed failed: value["kind"] = "failed"; value["message"] = failed.Message; break;
        }
        return value;
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var result) ? result.GetString()! : fallback;
    private static int Number(JsonElement value, string key, int fallback = 0) => value.TryGetProperty(key, out var result) ? result.GetInt32() : fallback;
    private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var result) && result.GetBoolean();
    private static async Task WaitAsync(Func<bool> condition, CancellationToken cancellation)
    { while (!condition()) await Task.Delay(1, cancellation); }
    /// <summary>The original gated renderer's effects: preparations ignore cancellation until
    /// released; enqueue records the revision; callbacks retain their original captured token.
    /// Additional stop recording observes the C# lifetime adapter without simulating hardware.</summary>
    private sealed class GatedRenderer : ISpeechPCMRendering
    {
        internal sealed record Preparation(ulong Revision, Func<long, Task> Played, Func<Task> Invalidated, TaskCompletionSource Waiter);
        internal List<Preparation> Preparations { get; } = [];
        internal List<ulong> Enqueues { get; } = [];
        internal List<(ulong Revision, bool Release)> Stops { get; } = [];
        internal Exception? StopError { get; set; }
        internal Action? CancellationCallback { get; init; }
        internal TaskCompletionSource? EnqueueGate { get; init; }
        internal TaskCompletionSource? PlayGate { get; init; }
        internal bool PlayEntered { get; private set; }
        public Task PrepareAsync(int sampleRate, double rate, ulong revision, Func<long, Task> played, Func<Task> invalidated, CancellationToken cancellationToken)
        {
            if (CancellationCallback is { } callback) _ = cancellationToken.Register(callback);
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Preparations.Add(new(revision, played, invalidated, waiter)); return waiter.Task;
        }
        public Task EnqueueAsync(SpeechPCMChunk chunk, long bufferId, ulong revision, CancellationToken cancellationToken) { Enqueues.Add(revision); return EnqueueGate?.Task ?? Task.CompletedTask; }
        public Task PlayAsync(ulong revision, CancellationToken cancellationToken) { PlayEntered = true; return PlayGate?.Task ?? Task.CompletedTask; }
        public Task StopAsync(ulong revision, bool release) { Stops.Add((revision, release)); return StopError is { } error ? Task.FromException(error) : Task.CompletedTask; }
        internal void Release(int index, bool fail = false)
        { if (fail) Preparations[index].Waiter.SetException(new SpeechException(SpeechFailure.InvalidAudio)); else Preparations[index].Waiter.SetResult(); }
        internal void ReleaseAll() { foreach (var preparation in Preparations) preparation.Waiter.TrySetResult(); }
        internal JsonObject Snapshot() => new() { ["preparations"] = JsonSerializer.SerializeToNode(Preparations.Select(preparation => preparation.Revision.ToString(CultureInfo.InvariantCulture))), ["enqueues"] = JsonSerializer.SerializeToNode(Enqueues.Select(revision => revision.ToString(CultureInfo.InvariantCulture))) };
    }
}
