using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

/// <summary>Original controller observations, including exact Foundation fingerprint/profile
/// bytes, drive the actual C# owner. Explicit effects remain in-memory; no native provider,
/// installed voice, recognition model, file transfer or audio device participates.</summary>
public sealed partial class LearningSpeechControllerTests
{
    [Fact]
    public async Task DestinationDisclosureUpdatesWithoutReadingCredentialsOrLeakingUrlSecrets()
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async _ =>
        {
            var settings = new LearningSpeechSettings(() => new AppSettings(), () => { }, new DisclosureCredentials()); var effects = new ValueEffects();
            await using var controller = new LearningSpeechController(settings, new SpeechTelemetry((_, _, _) => { }), effects, effects, effects, effects, effects, effects,
                _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
            int changes = 0; controller.PropertyChanged += (_, _) => changes++;
            Assert.True(controller.IsDestinationAvailable); Assert.Contains("stays on this device", controller.DestinationDescription);
            settings.SelectProvider(LearningSpeechProviderKind.Endpoint); settings.SetEndpointBaseUrl("https://example.org/private/path"); settings.SetEndpointModel("voice/model"); settings.SetEndpointVoice("speaker");
            Assert.True(controller.IsDestinationAvailable); Assert.Contains("https://example.org", controller.DestinationDescription); Assert.Contains("voice/model", controller.DestinationDescription);
            Assert.DoesNotContain("private/path", controller.DestinationDescription); Assert.True(changes > 0);
            settings.SetEndpointModel("voice?key=secret"); Assert.DoesNotContain("secret", controller.DestinationDescription);
            settings.SetEndpointBaseUrl("https://name:secret@example.org/v1?key=secret"); Assert.DoesNotContain("secret", controller.DestinationDescription);
            Assert.False(controller.IsDestinationAvailable); Assert.Contains("not configured", controller.DestinationDescription);
        });
    }
    private sealed class DisclosureCredentials : IOpenAICompatibleCredentialStore
    {
        public string? ReadApiKey() => throw new InvalidOperationException("Disclosure must not read credentials.");
        public void SaveApiKey(string? value) => throw new InvalidOperationException();
        public void DeleteApiKey() => throw new InvalidOperationException();
    }
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-controller-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Text(Corpus, "macReference"));
        Assert.Equal("en_US", Text(Corpus, "referenceLocale"));
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray();
        var ids = rows.Select(row => Text(row, "id")).ToArray();
        static string Hash(IEnumerable<string> values) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values.Order(StringComparer.Ordinal)))));
        Assert.Equal(104, rows.Length); Assert.Equal(104, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("0131513ed0b40ac58344f6e1aa28bb459042e9e381d872f654f21113a193d6f7", Hash(ids));
        Assert.Equal(42, rows.Count(row => Text(row, "scope") == "profile"));
        Assert.Equal(14, rows.Count(row => Text(row, "scope") == "identity"));
        Assert.Equal(48, rows.Count(row => Text(row, "scope") == "controller"));
        Assert.Equal(22, rows.Count(row => Text(row, "provenanceKind") == "original-test-adaptation"));
        Assert.Equal(82, rows.Count(row => Text(row, "provenanceKind") == "source-policy-extension"));
        var methods = rows.Where(row => Text(row, "provenanceKind") == "original-test-adaptation").Select(row => Text(row, "sourceTest")).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(21, methods.Length);
        Assert.Equal("0b9fc5c031d8d8886935611c48128173bc4c45ba8bdcb482d0e9659fefe92eac", Hash(methods));
        foreach (var row in rows)
        {
            string expectedSource = Text(row, "scope") == "profile" ? "Cotabby/Models/Speech/SpeechGenerationProfile.swift"
                : Text(row, "scope") == "identity" || Text(row, "id").Contains("export", StringComparison.Ordinal)
                    ? "Cotabby/Services/Speech/LearningSpeechController+Export.swift" : "Cotabby/Services/Speech/LearningSpeechController.swift";
            Assert.Equal(expectedSource, Text(row, "macSource"));
            if (Text(row, "provenanceKind") == "original-test-adaptation") Assert.Equal("controller", Text(row, "scope"));
            else Assert.False(row.TryGetProperty("sourceTest", out var reference) && reference.ValueKind != JsonValueKind.Null);
        }
        return ids.Select(id => new object[] { id });
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalControllerValues(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => Text(row, "id") == id);
        if (Text(row, "scope") == "controller") { await ControllerFlowAsync(row); return; }
        if (Text(row, "scope") == "profile")
        {
            JsonObject actual;
            try { byte[] bytes = SpeechExportIdentity.Profile(Profile(row.GetProperty("profile"))); actual = new() { ["json"] = Encoding.UTF8.GetString(bytes), ["digest"] = SpeechExportPolicy.Digest(bytes) }; }
            catch (JsonException) { actual = new() { ["error"] = "encodingFailed" }; }
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), id + "\n" + actual); return;
        }
        using var ui = new BatchUiContext();
        await ui.RunAsync(async _ =>
        {
            var saved = new AppSettings();
            if (row.TryGetProperty("seed", out var seed)) foreach (var value in seed.EnumerateObject())
            {
                var property = typeof(AppSettings).GetProperties().Single(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == value.Name);
                property.SetValue(saved, value.Value.Clone());
            }
            var settings = new LearningSpeechSettings(() => saved, () => { }, new Credentials()); var telemetry = new SpeechTelemetry((_, _, _) => { });
            var effects = new ValueEffects(); await using var controller = new LearningSpeechController(settings, telemetry, effects, effects, effects, effects, effects, effects,
                _ => new Dictionary<string, double>(), StringComparer.Ordinal, Path.GetTempPath(), () => effects);
            if (Text(row, "scope") != "identity") throw new InvalidOperationException("Controller trace runner under construction");
            JsonObject Observation() => new() { ["configurationID"] = controller.ExportConfigurationId,
                ["profileJSON"] = Encoding.UTF8.GetString(SpeechExportIdentity.Profile(settings.GenerationProfile)),
                ["revision"] = ((ulong)typeof(LearningSpeechController).GetField("_exportRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!).ToString(CultureInfo.InvariantCulture) };
            var trace = new JsonArray { Observation() };
            foreach (var action in row.GetProperty("actions").EnumerateArray()) { Apply(action, settings); trace.Add(Observation()); }
            var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
            Assert.True(JsonNode.DeepEquals(expected, trace), id + "\n" + string.Join("\n", AnkiReviewSessionTests.Differences(expected, trace).Take(15)));
        });
    }
    private static void Apply(JsonElement action, LearningSpeechSettings settings)
    {
        switch (Text(action, "op"))
        {
            case "provider": settings.SelectProvider(JsonSerializer.Deserialize<LearningSpeechProviderKind>(action.GetProperty("value"))); break;
            case "assets": settings.SetLocalAssets(Text(action, "model"), Text(action, "projection")); break;
            case "model": settings.SetLocalModelPath(Text(action, "value")); break;
            case "projection": settings.SetLocalProjectionPath(Text(action, "value")); break;
            case "reference": settings.SetReferenceAudioPath(Text(action, "value")); break;
            case "endpointURL": settings.SetEndpointBaseUrl(Text(action, "value")); break;
            case "endpointModel": settings.SetEndpointModel(Text(action, "value")); break;
            case "endpointVoice": settings.SetEndpointVoice(Text(action, "value")); break;
            case "rate": settings.SetRateMultiplier(Scalar(action.GetProperty("value"))); break;
            case "voice": settings.SetVoiceId(action.GetProperty("value").GetString(), LearningLanguages.FromCode(Text(action, "language"))!.Value); break;
            case "sampling": settings.SetSamplingOverrides(Sampling(action.GetProperty("value"))); break;
            case "saveKey": settings.SaveEndpointApiKey(action.GetProperty("value").GetString()); break;
            default: throw new InvalidOperationException("Unknown settings action");
        }
    }
    private static SpeechGenerationProfile Profile(JsonElement value) => new()
    {
        Sampling = value.TryGetProperty("sampling", out var sampling) ? Sampling(sampling) : new(),
        ReferenceAudioPath = value.TryGetProperty("referenceAudioPath", out var reference) ? reference.GetString() : null,
        EndpointVoice = value.TryGetProperty("endpointVoice", out var voice) ? voice.GetString() : null,
    };
    private static LearningModelSamplingOverrides Sampling(JsonElement value) => new(
        value.TryGetProperty("temperature", out var temperature) ? Scalar(temperature) : null,
        value.TryGetProperty("topP", out var topP) ? Scalar(topP) : null,
        value.TryGetProperty("topK", out var topK) ? topK.GetInt64() : null,
        value.TryGetProperty("repetitionPenalty", out var repetition) ? Scalar(repetition) : null,
        value.TryGetProperty("seedMode", out var mode) ? JsonSerializer.Deserialize<LearningModelSeedMode>(mode) : LearningModelSeedMode.Recommended,
        value.TryGetProperty("fixedSeed", out var seed) ? seed.GetUInt32() : 42);
    private static double Scalar(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : value.GetString() switch
    { "nan" => double.NaN, "infinity" => double.PositiveInfinity, "-infinity" => double.NegativeInfinity, "-0" => -0.0, _ => throw new InvalidOperationException("Unknown scalar") };
    private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var result) ? result.GetString()! : fallback;
    private sealed class Credentials : IOpenAICompatibleCredentialStore
    {
        private string? _key;
        public string? ReadApiKey() => _key;
        public void SaveApiKey(string? value) { _key = value is null ? null : FoundationText.TrimWhitespaceAndNewlines(value); if (_key?.Length == 0) _key = null; }
        public void DeleteApiKey() => _key = null;
    }
    /// <summary>Identity-only cases never activate any platform boundary. Throwing unexpected
    /// actions prevents a fake success from substituting for voice/audio/generator implementation.</summary>
    private sealed class ValueEffects : ISpeechProviding, ISpeechAudioGenerating, ISpeechAudioPlaying, ISpeechPCMPlaying, ISystemSpeechAudioExporting, ISpeechAudioDuration, ISpeechModelManagement
    {
        public Action<SpeechProviderEvent>? EventHandler { get; set; }
        public Action? DirectoryChanged { get; set; }
        public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
        public IReadOnlyList<SpeechVoice> RefreshVoices() => throw new InvalidOperationException("Unexpected voice catalog access");
        public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) => throw new InvalidOperationException("Unexpected system speech");
        public void Stop(SpeechPlaybackToken token) { }
        public void Stop() { }
        public void Shutdown() { }
        public Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected generation");
        public Task CancelAsync(SpeechPlaybackToken token) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token) => throw new InvalidOperationException("Unexpected encoded playback");
        public void Begin(int sampleRate, double rate, SpeechPlaybackToken token) => throw new InvalidOperationException("Unexpected PCM preparation");
        public Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected PCM");
        public Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected EOF");
        public Task<SpeechExportAudio> AudioAsync(string text, string voiceId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected system recording");
        public double Duration(ReadOnlySpan<byte> audio) => throw new InvalidOperationException("Unexpected duration decoder");
        public IReadOnlyList<RuntimeModelOption> InstalledModelOptions() => throw new InvalidOperationException("Unexpected model directory scan");
    }
}
