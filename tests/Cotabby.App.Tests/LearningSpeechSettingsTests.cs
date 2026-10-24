using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

/// <summary>The same explicit actions run through the compiled production preference owner.
/// Complete-original Swift observations own the oracle; this runner supplies only temporary
/// files/credentials and projects platform-specific temporary paths to the shared asset marker.</summary>
public sealed class LearningSpeechSettingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly Dictionary<string, PropertyInfo> RawProperties = typeof(AppSettings).GetProperties()
        .Select(p => (Property: p, Name: p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
        .Where(p => p.Name?.StartsWith("learningSpeech.", StringComparison.Ordinal) == true).ToDictionary(p => p.Name!, p => p.Property);
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-settings-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Text(Corpus, "macReference"));
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(144, rows.Length);
        Assert.Equal("2c31a6bcfade261b924c5899cae9cb0937ecbe5a428568eae4b0cf78b2580515", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { Text(r, "id") });
    }
    [Theory, MemberData(nameof(Cases))]
    public void CompleteOriginalSettingsAndIdentity(string id)
    {
        var input = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id);
        if (Text(input, "scope") == "identity") { Assert.Equal(Text(input, "expected"), Identity(input)); return; }
        string root = Path.Combine(Path.GetTempPath(), "lingrove-speech-settings-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            if (input.TryGetProperty("files", out var files)) foreach (var file in files.EnumerateArray())
            { using var output = File.Create(Path.Combine(root, Text(file, "path"))); output.SetLength(file.GetProperty("size").GetInt64()); }
            JsonElement row = JsonSerializer.SerializeToElement(Expand(JsonNode.Parse(input.GetRawText())!, root));
            var current = new AppSettings(); var credentials = new Credentials();
            foreach (var seed in row.GetProperty("seed").EnumerateObject())
            {
                var value = seed.Value.ValueKind == JsonValueKind.Object && seed.Value.TryGetProperty("$data", out var data) ? data.Clone() : seed.Value.Clone();
                RawProperties[seed.Name].SetValue(current, value);
            }
            if (row.TryGetProperty("seedProfiles", out var specs))
            {
                var profiles = specs.EnumerateArray().ToDictionary(Identity, p => p.GetProperty("profile").Clone(), CanonicalStringComparer.Instance);
                current.SpeechParametersValue = JsonSerializer.SerializeToElement(JsonSerializer.SerializeToUtf8Bytes(profiles));
            }
            var settings = new LearningSpeechSettings(() => current, () => { }, credentials);
            var invalidations = new JsonArray(); Action onInvalidation = () => invalidations.Add(Snapshot(settings, current, credentials)); settings.PlaybackInvalidated += onInvalidation;
            JsonObject Observe(object? result) => new() { ["state"] = Snapshot(settings, current, credentials), ["invalidations"] = invalidations.DeepClone(), ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions) };
            var trace = new JsonArray(Observe(null)); var remembered = new Dictionary<string, string>();
            foreach (var action in row.GetProperty("actions").EnumerateArray())
            {
                credentials.Fail = action.TryGetProperty("fail", out var fail) && fail.GetBoolean(); object? result = null;
                try
                {
                    switch (Text(action, "op"))
                    {
                        case "provider": settings.SelectProvider(JsonSerializer.Deserialize<LearningSpeechProviderKind>(action.GetProperty("value"))!); break;
                        case "assets": settings.SetLocalAssets(Text(action, "model"), Text(action, "projection")); break;
                        case "model": settings.SetLocalModelPath(Text(action, "value")); break;
                        case "projection": settings.SetLocalProjectionPath(Text(action, "value")); break;
                        case "reference": settings.SetReferenceAudioPath(Text(action, "value")); break;
                        case "endpointURL": settings.SetEndpointBaseUrl(Text(action, "value")); break;
                        case "endpointModel": settings.SetEndpointModel(Text(action, "value")); break;
                        case "endpointVoice": settings.SetEndpointVoice(Text(action, "value")); break;
                        case "rate": settings.SetRateMultiplier(Scalar(action.GetProperty("value"))); break;
                        case "voice": settings.SetVoiceId(action.TryGetProperty("value", out var voice) ? voice.GetString() : null, LearningLanguages.FromCode(Text(action, "language"))!.Value); break;
                        case "rememberIdentity": remembered[Text(action, "name")] = settings.ParameterIdentity; break;
                        case "sampling": settings.SetSamplingOverrides(Sampling(action.GetProperty("value")), action.TryGetProperty("expected", out var expectedId) ? remembered[expectedId.GetString()!] : null); break;
                        case "normalizeCurrent": settings.SetSamplingOverrides(settings.GenerationProfile.Sampling); break;
                        case "saveKey": settings.SaveEndpointApiKey(action.TryGetProperty("value", out var key) ? key.GetString() : null); break;
                        case "readKey": result = new Dictionary<string, object?> { ["key"] = settings.EndpointApiKey() }; break;
                        case "localConfiguration":
                            var local = settings.LocalConfiguration(); result = new Dictionary<string, object?> { ["model"] = local.ModelUrl.LocalPath, ["projection"] = local.ProjectionUrl.LocalPath, ["reference"] = local.ReferenceAudioUrl?.LocalPath }; break;
                        case "endpointConfiguration":
                            var endpoint = settings.EndpointConfiguration(); result = new { baseURL = endpoint.Endpoint.BaseUrl.AbsoluteUri, model = endpoint.Endpoint.ModelName, voice = endpoint.Voice, apiMode = "chatCompletions" }; break;
                        case "restart":
                            settings.PlaybackInvalidated -= onInvalidation; current = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(current))!;
                            settings = new(() => current, () => { }, credentials); settings.PlaybackInvalidated += onInvalidation; break;
                        default: throw new InvalidOperationException("Unknown settings action");
                    }
                }
                catch (OpenAICompatibleEndpointException error) { result = new { endpointError = error.Error switch { OpenAICompatibleEndpointError.InvalidBaseUrl => "invalidBaseURL", OpenAICompatibleEndpointError.InsecurePublicHttp => "insecurePublicHTTP", _ => "emptyModelName" } }; }
                catch (Exception error) when (error is SpeechException or CredentialFailure) { result = new { error = error.Message }; }
                trace.Add(Observe(result));
            }
            var actual = Normalize(trace, root); var expected = JsonNode.Parse(input.GetProperty("expected").GetRawText());
            Assert.True(JsonNode.DeepEquals(expected, actual), id + "\n" + string.Join("\n", AnkiReviewSessionTests.Differences(expected, actual).Take(15)));
        }
        finally { Directory.Delete(root, true); }
    }
    private static JsonNode? Expand(JsonNode? value, string root)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var s)) return JsonValue.Create(s.Replace("$ASSETS", root.Replace('\\', '/'), StringComparison.Ordinal));
        if (value is JsonArray a) return new JsonArray(a.Select(v => Expand(v, root)).ToArray());
        if (value is JsonObject o) return new JsonObject(o.Select(v => KeyValuePair.Create(v.Key, Expand(v.Value, root))));
        return value?.DeepClone();
    }
    private static JsonNode? Normalize(JsonNode? value, string root)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var s))
        {
            s = s.Replace(root, "$ASSETS", StringComparison.Ordinal).Replace(root.Replace('\\', '/'), "$ASSETS", StringComparison.Ordinal);
            return JsonValue.Create(s.StartsWith("$ASSETS", StringComparison.Ordinal) ? s.Replace('\\', '/') : s);
        }
        if (value is JsonArray a) return new JsonArray(a.Select(v => Normalize(v, root)).ToArray());
        if (value is JsonObject o) return new JsonObject(o.Select(v => KeyValuePair.Create(v.Key, Normalize(v.Value, root))));
        return value?.DeepClone();
    }
    private static JsonNode IdentityNode(string value)
    { try { return JsonNode.Parse(value)!; } catch (JsonException) { return JsonValue.Create(value)!; } }
    // Raw preference display mirrors JSONSerialization, which accepts UTF-16/32 and, on the
    // pinned source runtime, keeps the first duplicate property like its Codable reader.
    private static JsonNode? RawJson(byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false, true); int skip = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0) encoding = new UTF32Encoding(true, false, true);
        else if (bytes.Length >= 4 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) encoding = new UTF32Encoding(false, false, true);
        else if (bytes.Length >= 2 && bytes[0] == 0) encoding = new UnicodeEncoding(true, false, true);
        else if (bytes.Length >= 2 && bytes[1] == 0) encoding = new UnicodeEncoding(false, false, true);
        using var document = JsonDocument.Parse(encoding.GetString(bytes, skip, bytes.Length - skip));
        return Last(document.RootElement);
        static JsonNode? Last(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            { var result = new JsonObject(); foreach (var p in value.EnumerateObject()) if (!result.ContainsKey(p.Name)) result[p.Name] = Last(p.Value); return result; }
            if (value.ValueKind == JsonValueKind.Array) return new JsonArray(value.EnumerateArray().Select(Last).ToArray());
            return JsonNode.Parse(value.GetRawText());
        }
    }
    private static JsonObject Snapshot(LearningSpeechSettings s, AppSettings current, Credentials credentials)
    {
        var raw = new JsonObject();
        foreach (var p in RawProperties)
        {
            if (p.Value.GetValue(current) is not JsonElement value) continue;
            if (p.Key == "learningSpeech.parameters.v1" && value.ValueKind == JsonValueKind.String && value.TryGetBytesFromBase64(out var bytes))
            {
                JsonNode? decoded; try { decoded = RawJson(bytes); } catch (Exception error) when (error is JsonException or ArgumentException) { decoded = null; }
                raw[p.Key] = decoded is JsonObject profiles
                    ? new JsonObject { ["profiles"] = new JsonArray(profiles.OrderBy(p => p.Key, CanonicalStringComparer.Instance).Select(p => (JsonNode)new JsonObject { ["identity"] = IdentityNode(p.Key), ["profile"] = p.Value?.DeepClone() }).ToArray()) }
                    : new JsonObject { ["dataBase64"] = Convert.ToBase64String(bytes) };
            }
            else raw[p.Key] = JsonNode.Parse(value.GetRawText());
        }
        return new() { ["provider"] = JsonSerializer.SerializeToNode(s.SelectedProvider), ["localModel"] = s.LocalModelPath, ["localProjection"] = s.LocalProjectionPath, ["referenceAudio"] = s.ReferenceAudioPath,
            ["endpointURL"] = s.EndpointBaseUrl, ["endpointModel"] = s.EndpointModel, ["endpointVoice"] = s.EndpointVoice, ["rate"] = s.RateMultiplier,
            ["voices"] = JsonSerializer.SerializeToNode(s.VoiceIds), ["identity"] = IdentityNode(s.ParameterIdentity), ["profile"] = JsonSerializer.SerializeToNode(s.GenerationProfile, JsonOptions), ["raw"] = raw, ["keyReads"] = credentials.Reads, ["keySaves"] = credentials.Saves };
    }
    private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
    private static string Identity(JsonElement row) => ModelDestinationIdentity.Create(Text(row, "backend"), Text(row, "model"), Text(row, "baseURL"), Text(row, "mode"));
    private static double Scalar(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : value.GetString() switch { "nan" => double.NaN, "infinity" => double.PositiveInfinity, "-infinity" => double.NegativeInfinity, _ => throw new InvalidOperationException("Invalid fixture number") };
    private static LearningModelSamplingOverrides Sampling(JsonElement r) => new(r.TryGetProperty("temperature", out var t) ? Scalar(t) : null, r.TryGetProperty("topP", out var p) ? Scalar(p) : null, r.TryGetProperty("topK", out var k) ? k.GetInt32() : null, r.TryGetProperty("repetitionPenalty", out var penalty) ? Scalar(penalty) : null,
        JsonSerializer.Deserialize<LearningModelSeedMode>(r.GetProperty("seedMode")), r.GetProperty("fixedSeed").GetUInt32());
    [Fact]
    public void CanonicalProfileEqualityAndRetainedVoiceSnapshotsPreserveOriginalValues()
    {
        var composed = new SpeechGenerationProfile { ReferenceAudioPath = "/é.wav", EndpointVoice = "é" };
        var decomposed = new SpeechGenerationProfile { ReferenceAudioPath = "/e\u0301.wav", EndpointVoice = "e\u0301" };
        Assert.Equal(composed, decomposed); Assert.Equal(composed.GetHashCode(), decomposed.GetHashCode());
        Assert.Equal("e\u0301", decomposed.EndpointVoice);
        var values = new AppSettings(); var settings = new LearningSpeechSettings(() => values, () => { }, new Credentials());
        settings.SetVoiceId("old", LearningLanguage.English); var retained = settings.VoiceIds;
        settings.SetVoiceId("new", LearningLanguage.English); Assert.Equal("old", retained["en"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)retained)["en"] = "tampered");
        Assert.Equal("new", settings.VoiceId(LearningLanguage.English));
    }
    [Fact]
    public void GeneralResetRetainsEverySpeechKeyAndExplicitSecretsStayOutsideTheDocument()
    {
        var values = new AppSettings();
        foreach (var property in RawProperties.Values) property.SetValue(values, JsonSerializer.SerializeToElement("retained"));
        var reset = values.ResetGeneralPreferences(12);
        foreach (var property in RawProperties.Values)
            Assert.Equal(((JsonElement)property.GetValue(values)!).GetRawText(), ((JsonElement)property.GetValue(reset)!).GetRawText());
        var credentials = new Credentials(); var settings = new LearningSpeechSettings(() => reset, () => { }, credentials);
        Assert.Equal(0, credentials.Reads); settings.SaveEndpointApiKey(" unique-fixture-secret ");
        Assert.DoesNotContain("unique-fixture-secret", JsonSerializer.Serialize(reset)); Assert.Equal(0, credentials.Reads);
        Assert.Equal("unique-fixture-secret", settings.EndpointApiKey());
    }
    [Fact]
    public void SamplerPublicationFollowsDurableCommitAndDoesNotInvalidateTheCapturedProfile()
    {
        var values = new AppSettings(); var settings = new LearningSpeechSettings(() => values, () => { }, new Credentials());
        settings.SelectProvider(LearningSpeechProviderKind.Local); var before = settings.GenerationProfile; string identity = settings.ParameterIdentity;
        int invalidations = 0, publications = 0; settings.PlaybackInvalidated += () => invalidations++;
        settings.PropertyChanged += (_, _) =>
        {
            publications++;
            var data = values.SpeechParametersValue!.Value.GetBytesFromBase64();
            Assert.Equal(12, SpeechProfileCodec.Decode(data)![identity].Sampling.TopK);
        };
        settings.SetSamplingOverrides(new(TopK: 12)); settings.SetSamplingOverrides(new(TopK: 12));
        Assert.Equal(0, invalidations); Assert.Equal(1, publications); Assert.Null(before.Sampling.TopK);
    }
    private sealed class CredentialFailure() : Exception("fixture credential failure");
    private sealed class Credentials : IOpenAICompatibleCredentialStore
    {
        private string? _key; internal bool Fail; internal int Reads, Saves;
        public string? ReadApiKey() { Reads++; if (Fail) throw new CredentialFailure(); return _key; }
        public void SaveApiKey(string? value) { Saves++; if (Fail) throw new CredentialFailure(); _key = value is null ? null : FoundationText.TrimWhitespaceAndNewlines(value); if (_key?.Length == 0) _key = null; }
        public void DeleteApiKey() => _key = null;
    }
}
