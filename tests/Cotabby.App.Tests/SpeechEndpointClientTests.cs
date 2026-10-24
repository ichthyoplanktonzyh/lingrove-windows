using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

/// <summary>The actual speech HTTP client consumes original URLSession observations through
/// explicit in-memory HTTP/stream effects. Byte payloads remain bounded, disposal is asserted,
/// and source-only URLSession configuration observations are not fabricated as Windows output.</summary>
public sealed partial class SpeechEndpointClientTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-endpoint-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Text(Corpus, "macReference")); Assert.Equal("en_US", Text(Corpus, "referenceLocale"));
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); var ids = rows.Select(row => Text(row, "id")).ToArray();
        Assert.Equal(38, rows.Length); Assert.Equal(38, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("77f0207771093fcef58a9d61ba45738b5030de358afbc862943e2e803ab0f74b", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ids.Order(StringComparer.Ordinal))))));
        Assert.Equal(6, rows.Count(row => Text(row, "provenanceKind") == "original-test-adaptation")); Assert.Equal(32, rows.Count(row => Text(row, "provenanceKind") == "source-policy-extension"));
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            Assert.Equal("Cotabby/Services/Speech/SpeechEndpointClient.swift", Text(row, "macSource")); Assert.Equal(Text(row, "id").StartsWith("wire-", StringComparison.Ordinal), Flag(row, "wire"));
            if (Text(row, "provenanceKind") == "original-test-adaptation") { Assert.True(Flag(row, "original")); methods.Add(Text(row, "sourceTest")); }
            else { Assert.False(row.TryGetProperty("sourceTest", out _)); Assert.False(Flag(row, "original")); }
        }
        Assert.Equal(new[] { "cancellationStopsAResponseThatNeverCompletes", "rejectsHTTPFailuresRedirectsAndDeclaredOrStreamedOversize", "sendsOnlyExplicitSpeechFields" }.Select(method => "CotabbyTests/Services/Speech/SpeechEndpointClientTests.swift:" + method).Order(StringComparer.Ordinal), methods.Order(StringComparer.Ordinal));
        return ids.Select(id => new object[] { id });
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalSpeechHttpObservations(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => Text(row, "id") == id); bool original = Flag(row, "original");
        await using var wire = Flag(row, "wire") ? new LoopbackSpeechServer(row) : null;
        using var cancellation = new CancellationTokenSource(); if (Text(row, "cancel") == "before-start") cancellation.Cancel();
        var handler = new ScriptedHandler(row); int creations = 0;
        var client = wire is null ? new SpeechEndpointClient(() => { creations++; return handler; }) : new SpeechEndpointClient(); JsonObject result;
        try
        {
            var call = client.AudioAsync(Text(row, "text", "Hello."), Configuration(wire is null ? Text(row, "baseURL", "https://" + id + ".test") : wire.BaseUrl + "/" + id, Text(row, "model", "speech-model"), Text(row, "voice", "voice-a")), row.TryGetProperty("key", out var key) ? key.GetString() : "fixture-key", cancellation.Token);
            if (Text(row, "cancel") == "after-start") { await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await handler.Stream!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); }
            byte[] audio = await call;
            if (original && id == "original-request")
            {
                var echo = JsonNode.Parse(audio)!.AsObject(); echo["body"] = JsonNode.Parse(echo["body"]!.GetValue<string>()); result = new() { ["echo"] = echo };
            }
            else result = new() { ["bytes"] = audio.Length, ["digest"] = SpeechExportPolicy.Digest(audio) };
        }
        catch (OperationCanceledException) { result = new() { ["failure"] = "cancelled" }; }
        catch (SpeechException error)
        {
            result = new() { ["failure"] = error.Failure switch { SpeechFailure.EndpointStatus => "endpointStatus", SpeechFailure.ResponseTooLarge => "responseTooLarge", SpeechFailure.InvalidAudio => "invalidAudio", _ => throw new InvalidOperationException("Unexpected category") }, ["message"] = error.Message };
            if (error.Status is { } status) result["status"] = status;
        }
        catch (Exception error) when (error is HttpRequestException or IOException) { result = new() { ["failure"] = "transport" }; }
        var expected = row.GetProperty("expected");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("result").GetRawText()), result), id + "\n" + result);
        var request = original ? null : wire is null ? handler.Request : Assert.Single(wire.Records);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("request").GetRawText()), request), id + "\n" + request);
        // Cookies/cache are native URLSession observations; the real SocketsHttpHandler case below
        // verifies Windows transport behavior. Only actual shared bound/deadline constants join here.
        Assert.Equal(SpeechEndpointClient.MaximumBytes, expected.GetProperty("transfer").GetProperty("maximum").GetInt32());
        Assert.Equal(SpeechEndpointClient.ResourceTimeout.TotalSeconds, expected.GetProperty("transfer").GetProperty("resource").GetDouble());
        if (creations > 0) { Assert.True(handler.Disposed); if (handler.Stream is not null) Assert.True(handler.Stream.Disposed); }
        else if (wire is null) Assert.Equal("before-start", Text(row, "cancel"));
    }
    private static SpeechEndpointConfiguration Configuration(string url = "https://probe.test", string model = "speech-model", string voice = "voice-a") => new(new(url, model, OpenAICompatibleApiMode.ChatCompletions), voice);
    private static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var value) ? value.GetString()! : fallback;
    private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private sealed class ScriptedHandler(JsonElement row) : HttpMessageHandler
    {
        internal JsonObject? Request { get; private set; }
        internal FixtureStream? Stream { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken); bool original = Flag(row, "original"); string host = request.RequestUri!.Host;
            Request = new() { ["url"] = request.RequestUri.AbsoluteUri, ["method"] = request.Method.Method, ["body"] = JsonNode.Parse(body), ["authorization"] = request.Headers.Authorization?.ToString() ?? "", ["accept"] = string.Join(", ", request.Headers.Accept), ["contentType"] = request.Content.Headers.ContentType!.ToString(), ["idle"] = SpeechEndpointClient.IdleTimeout.TotalSeconds };
            if (Flag(row, "networkError")) throw new HttpRequestException("fixture connection lost");
            int status = row.TryGetProperty("status", out var code) ? code.GetInt32() : original && host == "failure.test" ? 401 : original && host == "redirect.test" ? 302 : 200;
            long count = row.TryGetProperty("chunks", out var chunks) ? chunks.EnumerateArray().Sum(chunk => chunk.GetInt64()) : 3;
            if (original && host == "streamed.test") count = 33L * 1024 * 1024;
            byte[]? echo = original && host == "request.test" ? JsonSerializer.SerializeToUtf8Bytes(new { path = request.RequestUri.AbsolutePath, method = request.Method.Method, authorization = request.Headers.Authorization?.ToString() ?? "", body }) : null;
            Stream = new(count, Flag(row, "waiting") || original && host == "waiting.test", echo); Entered.TrySetResult();
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StreamContent(Stream) };
            response.Content.Headers.ContentType = new(Text(row, "contentType", "audio/wav"));
            long? length = row.TryGetProperty("declared", out var declared) ? declared.ValueKind == JsonValueKind.Null ? null : declared.GetInt64() : echo?.LongLength ?? count;
            if (original && host == "oversized.test") length = SpeechEndpointClient.MaximumBytes + 1L;
            if (original && host == "streamed.test") length = null;
            response.Content.Headers.ContentLength = length;
            if (status is >= 300 and <= 399) response.Headers.Location = new("https://redirected.test/audio/speech");
            return response;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class FixtureStream(long count, bool waiting = false, byte[]? payload = null) : Stream
    {
        private long _offset;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); cancellationToken.ThrowIfCancellationRequested();
            if (waiting) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            int size = (int)Math.Min(buffer.Length, (payload?.LongLength ?? count) - _offset);
            if (payload is not null) payload.AsMemory((int)_offset, size).CopyTo(buffer); else buffer.Span[..size].Fill(90);
            _offset += size; return size;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
