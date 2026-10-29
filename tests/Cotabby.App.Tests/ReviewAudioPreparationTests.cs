using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Review;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

/// <summary>Complete source preparation traces with explicit held exports. Fakes supply only
/// speech effects; the actual owner decides reuse/busy/cancellation/publication and audition.</summary>
public sealed class ReviewAudioPreparationTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "review-audio-v1.json")));
        Assert.Equal("en_US", d.RootElement.GetProperty("referenceLocale").GetString()); Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(46, rows.Length);
        Assert.Equal("1a37df193e251fd7289ec15bd109172d33153548e1afdc3afc07485d7d2361ac", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    private static string Text(JsonElement a, string key, string fallback) => a.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;
    private static string? Optional(JsonElement a, string key) => a.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static bool Flag(JsonElement a, string key) => a.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Kind(ReviewAudioKind kind) => kind == ReviewAudioKind.Expression ? "expression" : "sentence";
    private static object Audio(ReviewPreparedAudio value) => new { fingerprint = value.Fingerprint, bytes = value.Audio.ByteCount, extension = value.Audio.FileExtension, sha256 = Convert.ToHexStringLower(SHA256.HashData(value.Audio.CopyBytes())) };
    private static SpeechExportRequest Request(JsonElement a) => new(Text(a, "text", "clicked text"), LearningLanguages.FromCode(Text(a, "language", "en"))!.Value, Text(a, "fingerprint", "fp"), new SpeechExportRequest.Source.System("fixture"));
    [Theory, MemberData(nameof(Cases))]
    public async Task OriginalPreparationTrace(string id, JsonElement row)
    {
        var fake = new FakeSpeech(); var owner = new ReviewAudioPreparation(row.GetProperty("speech").GetBoolean() ? fake : null); fake.OwnerId = owner.OwnerId;
        object Snapshot() => new { generating = owner.Generating is { } g ? Kind(g) : null, prepared = owner.Prepared.ToDictionary(p => Kind(p.Key), p => Audio(p.Value)), calls = fake.Calls.ToArray() };
        var trace = new List<object> { new { state = Snapshot(), outcome = (object?)null } };
        Task<ReviewPreparedAudio>? pending = null; CancellationTokenSource? pendingCancellation = null;
        foreach (var a in row.GetProperty("actions").EnumerateArray())
        {
            var kind = Text(a, "kind", "expression") == "expression" ? ReviewAudioKind.Expression : ReviewAudioKind.Sentence;
            fake.Failure = Optional(a, "fail"); fake.Fingerprint = Text(a, "fingerprint", "request-fingerprint"); object? outcome = null;
            try
            {
                switch (Text(a, "op", ""))
                {
                    case "error": throw new SpeechException(Enum.Parse<SpeechFailure>(Text(a, "fail", ""), true), Optional(a, "text") is { } status ? int.Parse(status, CultureInfo.InvariantCulture) : null);
                    case "request":
                        var language = LearningLanguages.FromCode(Text(a, "language", "en"))!.Value;
                        var entry = new VocabularyEntry(Guid.NewGuid(), "entry", "entry", language, VocabularyEntryKind.Word, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                        var content = new ReviewCardContent(Text(a, "text", "expression"), "answer", Text(a, "sentence", "sentence"), [], "translation", "", "", "", "", false, false);
                        var value = owner.Request(kind, entry, content); outcome = new { text = value.Text, language = value.Language.Code(), fingerprint = value.Fingerprint }; break;
                    case "prepare": case "audition":
                        using (var cancellation = new CancellationTokenSource())
                        {
                            if (Flag(a, "cancel")) cancellation.Cancel(); var request = Request(a);
                            if (Text(a, "op", "") == "audition") await owner.AuditionAsync(kind, request, cancellation.Token);
                            else outcome = Audio(await owner.PrepareAsync(kind, request, Flag(a, "force"), cancellation.Token));
                        }
                        break;
                    case "start":
                        fake.BeginHold(); pendingCancellation = new(); pending = owner.PrepareAsync(kind, Request(a), Flag(a, "force"), pendingCancellation.Token);
                        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); break;
                    case "cancelPending": pendingCancellation!.Cancel(); break;
                    case "complete":
                        var work = pending!; pending = null; fake.Release();
                        try { outcome = Audio(await work); } finally { pendingCancellation!.Dispose(); pendingCancellation = null; }
                        break;
                    case "discard": owner.Discard(); break;
                    case "stop": owner.StopPreview(); break;
                    default: throw new InvalidOperationException("Unknown preparation action");
                }
            }
            catch (OperationCanceledException) { outcome = new { error = "cancelled" }; }
            catch (SpeechException error) { outcome = new { error = error.Message }; }
            trace.Add(new { state = Snapshot(), outcome });
        }
        Assert.Null(pending); Assert.Null(pendingCancellation); Assert.Null(fake.Waiter);
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    [Fact]
    public async Task ReleasedSheetSnapshotAndOtherOwnersKeepTheirOwnRecordings()
    {
        var speech = new FakeSpeech(); var first = new ReviewAudioPreparation(speech); var second = new ReviewAudioPreparation(speech);
        var request = new SpeechExportRequest("text", LearningLanguage.English, "fp", new SpeechExportRequest.Source.System("voice"));
        var value = await first.PrepareAsync(ReviewAudioKind.Expression, request); var snapshot = first.Prepared;
        first.Discard(); Assert.Empty(first.Prepared); Assert.Same(value, snapshot[ReviewAudioKind.Expression]); Assert.Empty(second.Prepared); Assert.NotEqual(first.OwnerId, second.OwnerId);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<ReviewAudioKind, ReviewPreparedAudio>)snapshot).Clear());
    }
    [Fact]
    public void FrozenGeneratedRequestKeepsSamplerAndSecretOutOfAutomaticDiagnostics()
    {
        const string secret = "synthetic-speech-only-secret";
        var configuration = new SpeechEndpointConfiguration(new OpenAICompatibleEndpointConfiguration("http://127.0.0.1:8000/v1", "speech", OpenAICompatibleApiMode.ChatCompletions), "speaker");
        var endpoint = new SpeechAudioSource.Endpoint(configuration, secret); var sampling = new LearningModelSamplingOverrides(TopK: 12);
        var request = new SpeechExportRequest("clicked private text", LearningLanguage.Japanese, "fingerprint", new SpeechExportRequest.Source.Generated(endpoint, sampling));
        sampling = sampling with { TopK = 20 };
        Assert.Equal(12, ((SpeechExportRequest.Source.Generated)request.AudioSource).Sampling.TopK); Assert.Equal(secret, endpoint.ApiKey);
        Assert.DoesNotContain(secret, endpoint.ToString()); Assert.DoesNotContain(secret, JsonSerializer.Serialize(endpoint)); Assert.DoesNotContain("clicked private text", request.ToString());
    }
    [Fact]
    public void ProviderTokensAreUniqueExplicitValues()
    {
        var first = new SpeechPlaybackToken(); var second = new SpeechPlaybackToken();
        Assert.NotEqual(Guid.Empty, first.RawValue); Assert.NotEqual(first, second); Assert.Equal(first, new SpeechPlaybackToken(first.RawValue));
    }
    private sealed class FakeSpeech : ISpeechExporting
    {
        public string ExportConfigurationId => "fixture"; public Guid OwnerId; public string? Failure; public string Fingerprint = "request-fingerprint";
        public List<Dictionary<string, object?>> Calls { get; } = [];
        private bool _hold; public TaskCompletionSource<SpeechExportAudio>? Waiter; public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpeechExportRequest ExportRequest(string text, LearningLanguage? language, string? context = null)
        {
            Calls.Add(new() { ["op"] = "request", ["text"] = text, ["language"] = language?.Code(), ["context"] = context });
            if (Failure == "request") throw new SpeechException(SpeechFailure.GenerationFailed);
            return new(text, language ?? throw new InvalidOperationException("Missing language"), Fingerprint, new SpeechExportRequest.Source.System("fixture"));
        }
        public Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(new() { ["op"] = "export", ["text"] = request.Text, ["language"] = request.Language.Code(), ["fingerprint"] = request.Fingerprint });
            if (_hold) { Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously); Entered.TrySetResult(); return Waiter.Task; }
            if (Failure == "export") return Task.FromException<SpeechExportAudio>(new SpeechException(SpeechFailure.GenerationFailed));
            if (Failure == "cancel") return Task.FromException<SpeechExportAudio>(new OperationCanceledException());
            return Task.FromResult(Audio());
        }
        private SpeechExportAudio Audio() => new(Encoding.UTF8.GetBytes("audio-" + Calls.Count(c => Equals(c["op"], "export"))), "wav");
        public void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language)
        {
            Calls.Add(new() { ["op"] = "audition", ["ownerMatches"] = OwnerId == ownerId, ["language"] = language.Code(), ["bytes"] = audio.ByteCount });
            if (Failure == "audition") throw new SpeechException(SpeechFailure.GenerationFailed);
        }
        public void Stop(Guid ownerId) => Calls.Add(new() { ["op"] = "stop", ["ownerMatches"] = OwnerId == ownerId });
        public void BeginHold() { _hold = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public void Release() { var next = Waiter!; Waiter = null; _hold = false; if (Failure == "export") next.SetException(new SpeechException(SpeechFailure.GenerationFailed)); else next.SetResult(Audio()); }
    }
}
