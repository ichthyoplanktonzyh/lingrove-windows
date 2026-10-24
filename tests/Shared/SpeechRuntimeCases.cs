using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Xunit;

namespace Cotabby.Tests.Speech;

internal static class SpeechRuntimeCases
{
    internal static IEnumerable<object[]> Data()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-runtime-v1.json")));
        var root = corpus.RootElement; Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", root.GetProperty("macReference").GetString()); Assert.Equal("en_US", root.GetProperty("referenceLocale").GetString());
        var rows = root.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(17, rows.Length);
        var ids = rows.Select(row => row.GetProperty("id").GetString()!).ToArray(); Assert.Equal(17, ids.Distinct().Count());
        Assert.Equal("068fcafb0e9f9e2f8f91bd340f8b39af6b2bbf20805b4396a2cad48d730281b8", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ids.Order(StringComparer.Ordinal))))));
        Assert.Equal(11, rows.Count(row => row.GetProperty("provenanceKind").GetString() == "original-test-adaptation"));
        Assert.Equal(6, rows.Count(row => row.GetProperty("provenanceKind").GetString() == "source-policy-extension"));
        Assert.Equal(10, rows.Where(row => row.TryGetProperty("sourceTest", out _)).Select(row => row.GetProperty("sourceTest").GetString()).Distinct().Count());
        var provenance = rows.Where(row => row.TryGetProperty("sourceTest", out _)).Select(row => row.GetProperty("id").GetString() + "|" + row.GetProperty("sourceTest").GetString()).Order(StringComparer.Ordinal);
        Assert.Equal("ac0ead0ad1c395cc06fe45439c867d9394a9048d3572aac79bd9d468df540cce", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", provenance)))));
        foreach (var row in rows)
        {
            Assert.Equal("Cotabby/Services/Speech/LocalSpeechRuntime.swift", row.GetProperty("macSource").GetString());
            bool original = row.GetProperty("provenanceKind").GetString() == "original-test-adaptation";
            Assert.Equal(original, row.TryGetProperty("sourceTest", out var test));
            if (original) Assert.StartsWith("CotabbyTests/Services/Speech/LocalSpeechRuntimeTests.swift:", test.GetString());
            yield return new object[] { row.GetProperty("id").GetString()!, row.GetProperty("expected").GetRawText() };
        }
    }

    internal static async Task RunAsync(string id, string expected, Func<SpeechRuntimeFixture, ISpeechHelperProcessFactory>? factory = null)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var gate = new SpeechTestGate(); using var releaseOnTimeout = lifetime.Token.Register(gate.Release);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chunks = new ConcurrentQueue<int>(); var results = new List<string>(); var known = new Dictionary<string, string>();
        await using var fixture = new SpeechRuntimeFixture(factory, id == "idle" ? TimeSpan.FromMilliseconds(30) : null,
            id == "timeout" ? TimeSpan.FromSeconds(2) : null, async value =>
            {
                if (value.Snapshot.Phase == SpeechLocalRuntimePhase.Stopping) stopped.TrySetResult();
                if (value.Snapshot.Phase == SpeechLocalRuntimePhase.Unloaded) unloaded.TrySetResult();
                if (id == "blocked-events") await gate.WaitAsync();
            });
        var runtime = fixture.Runtime;
        SpeechAudioRequest Request(string text, LearningModelSamplingOverrides? sampling = null, LearningLanguage language = LearningLanguage.English)
        { var request = fixture.Request(text, sampling, language); known.Add(text, request.Token.RawValue.ToString().ToUpperInvariant()); return request; }
        async Task<byte[]> Collect(SpeechAudioRequest request, SpeechLocalConfiguration? configuration = null)
        {
            using var buffer = new MemoryStream();
            await runtime.StreamAsync(request, configuration ?? fixture.Configuration, chunk =>
            {
                chunks.Enqueue(chunk.ByteCount);
                if (id == "receiver-reject" && request.Text == "reject") throw new IOException("Fixture receiver rejected audio.");
                buffer.Write(chunk.CopyBytes()); return Task.CompletedTask;
            }, lifetime.Token); return buffer.ToArray();
        }
        void Success(byte[] bytes) => results.Add("ok:" + bytes.Length + ":" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
        async Task Run(string text, SpeechLocalConfiguration? configuration = null, LearningModelSamplingOverrides? sampling = null)
        { try { Success(await Collect(Request(text, sampling), configuration)); } catch (Exception) { results.Add("failed"); } }
        try
        {
            switch (id)
            {
                case "reuse": await Run("first"); await Run("second"); break;
                case "sampling": await Run("first", sampling: new(0.1, 0.7, 12, 1.1, LearningModelSeedMode.Fixed, 81)); await Run("second"); break;
                case "sampling-bounds": await Run("你好，héllo.\nNext", sampling: new(9, -1, 999, -1, LearningModelSeedMode.Random, 81)); break;
                case "reference": await Run("reference", fixture.Configuration with { ReferenceAudioUrl = new Uri(Path.Combine(fixture.Root, "speaker 声音.wav")) }); break;
                case "configuration": await Run("first"); await Run("second", fixture.Configuration with { ModelUrl = new Uri(Path.Combine(fixture.Root, "different.gguf")) }); break;
                case "unsupported":
                    var error = await Assert.ThrowsAsync<SpeechException>(() => Collect(Request("unsupported", language: LearningLanguage.Dutch)));
                    Assert.Equal(SpeechFailure.UnsupportedLanguage, error.Failure); results.Add("failed"); break;
                case "pre-cancel":
                    using (var cancellation = new CancellationTokenSource())
                    { cancellation.Cancel(); var request = Request("pre-cancel"); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StreamAsync(request, fixture.Configuration, _ => Task.CompletedTask, cancellation.Token)); results.Add("cancelled"); }
                    break;
                case "idle": await Run("first"); await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Run("second"); break;
                case "timeout": await Run("ignore-term"); await Run("after"); break;
                case "blocked-events": await Run("first"); await runtime.ShutdownAsync(); gate.Release(); break;
                case "receiver-reject": await Run("reject"); await Run("after"); break;
                case "cancel": case "stream-cancel": case "blocked-receiver": case "cancel-drain":
                    string text = id switch { "stream-cancel" => "chunk-then-wait", "blocked-receiver" => "first", "cancel-drain" => "ignore-term", _ => "wait" };
                    var first = Request(text); using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        using var data = new MemoryStream();
                        var job = runtime.StreamAsync(first, fixture.Configuration, async chunk => { chunks.Enqueue(chunk.ByteCount); data.Write(chunk.CopyBytes()); if (id == "blocked-receiver") await gate.WaitAsync(); }, cancellation.Token);
                        try
                        {
                            if (id == "blocked-receiver") await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            else if (id == "stream-cancel") await SpeechRuntimeFixture.WaitAsync(() => !chunks.IsEmpty);
                            else await fixture.WaitForRequestAsync(text);
                            if (id == "cancel-drain")
                            {
                                var second = Request("second"); using var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                                var next = runtime.StreamAsync(second, fixture.Configuration, _ => Task.CompletedTask, nextCancellation.Token);
                                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)); nextCancellation.Cancel();
                                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next); results.Add("cancelled");
                                await Assert.ThrowsAnyAsync<Exception>(() => job); results.Add("failed"); await Run("after");
                            }
                            else
                            {
                                if (id != "blocked-receiver") cancellation.Cancel(); await runtime.CancelAsync(first.Token);
                                await Assert.ThrowsAnyAsync<Exception>(() => job); results.Add("failed");
                                if (id != "stream-cancel") await Run("after");
                            }
                        }
                        finally { cancellation.Cancel(); gate.Release(); try { await job; } catch (Exception) { } }
                    }
                    break;
                default: await Run(id); await Run("after"); break;
            }
            await runtime.ShutdownAsync(); gate.Release(); await runtime.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var parameters = fixture.Parameters().Select(row =>
            {
                var value = JsonNode.Parse(row.GetRawText())!.AsObject(); value["matchingId"] = known.TryGetValue(value["text"]!.GetValue<string>(), out var expectedId) && expectedId == value["id"]!.GetValue<string>(); value.Remove("id");
                if (value["speaker_audio_path"] is { } path) value["speaker_audio_path"] = "$ROOT/" + Path.GetFileName(path.GetValue<string>());
                return value;
            }).ToArray();
            var starts = fixture.Lines("starts");
            var actual = JsonSerializer.SerializeToNode(new { results, chunks = chunks.ToArray(), starts = starts.Length, reaped = starts.All(Reaped), parameters });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), id + "\nexpected=" + expected + "\nactual=" + actual);
        }
        finally { gate.Release(); }
    }
    private static bool Reaped(string value)
    { try { using var process = Process.GetProcessById(int.Parse(value)); return process.HasExited; } catch (ArgumentException) { return true; } }
}
