using System.Net;
using System.Text;
using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Win32.Review;

namespace Cotabby.App.Tests;

/// <summary>The same synthetic scenarios run through the complete Mac client with URLProtocol
/// and the actual Windows HTTP service with a handler. Neither runner opens a listening socket.</summary>
public sealed class AnkiConnectParityTests
{
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 512 };
    public static IEnumerable<object[]> Cases()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-connect-v1.json")));
        return corpus.RootElement.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task OriginalClient(string id, JsonElement row)
    {
        var result = new Dictionary<string, object?>(); string kind = Text(row, "kind", "perform");
        if (kind == "endpoint") result["endpoint"] = AnkiArchiveEndpointPolicy.Normalize(Text(row, "endpoint"));
        else if (kind == "encode")
        {
            try
            {
                var bytes = AnkiWirePolicy.EncodeBody(new Dictionary<string, AnkiValue> { ["payload"] = Payload(row) });
                result["valid"] = true; result["count"] = bytes.Length;
                if (bytes.Length < 4096) { result["json"] = JsonSerializer.SerializeToElement<AnkiValue>(JsonSerializer.Deserialize<AnkiValue>(bytes, Options)!, Options); result["raw"] = Encoding.UTF8.GetString(bytes); }
            }
            catch (JsonException) { result["valid"] = false; result["kind"] = "encoding"; }
            catch (AnkiReviewException error) { result["valid"] = false; result["error"] = error.Message; }
        }
        else
        {
            using var handler = new ScenarioHandler(row); using var client = new AnkiConnectClient(handler);
            var payload = Payload(row); var parameters = payload.Object ?? new Dictionary<string, AnkiValue> { ["payload"] = payload };
            try
            {
                var answer = await client.PerformAsync(Text(row, "action", "version"), parameters,
                    new AnkiConnection(Text(row, "endpoint", "http://127.0.0.1:8765"), Text(row, "key")));
                result["valid"] = true; result["result"] = JsonSerializer.SerializeToElement<AnkiValue>(answer, Options);
            }
            catch (JsonException) { result["valid"] = false; result["kind"] = "encoding"; }
            catch (OperationCanceledException) { result["valid"] = false; result["kind"] = "cancelled"; }
            catch (AnkiReviewException error) { result["valid"] = false; result["error"] = error.Message; }
            result["requests"] = handler.Count;
            if (handler.Body is not null)
            {
                result["method"] = handler.Method; result["contentType"] = handler.ContentType; result["timeout"] = client.RequestTimeout.TotalSeconds;
                result["bodyCount"] = handler.Body.Length;
                if (handler.Body.Length < 4096) result["body"] = JsonSerializer.SerializeToElement<AnkiValue>(JsonSerializer.Deserialize<AnkiValue>(handler.Body, Options)!, Options);
            }
        }
        using var actual = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(result, Options));
        Assert.True(Same(row.GetProperty("expected"), actual.RootElement), id + "\nExpected: " + row.GetProperty("expected") + "\nActual: " + actual.RootElement);
    }
    internal static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var v) ? v.GetString()! : fallback;
    private static int Count(JsonElement row, string key, int fallback = 0) => row.TryGetProperty(key, out var v) ? v.GetInt32() : fallback;
    private static AnkiValue Payload(JsonElement row)
    {
        if (row.TryGetProperty("repeatCount", out var repeat))
            return new AnkiValue.Text(string.Concat(Enumerable.Repeat(Text(row, "repeatText", "a"), repeat.GetInt32())));
        if (row.TryGetProperty("specialNumber", out var special)) return new AnkiValue.Number(special.GetString() == "nan" ? double.NaN : double.PositiveInfinity);
        return row.TryGetProperty("params", out var value) ? value.Deserialize<AnkiValue>(Options)! : new AnkiValue.Map(new Dictionary<string, AnkiValue>());
    }
    private static bool Same(JsonElement left, JsonElement right) => left.ValueKind == right.ValueKind && left.ValueKind switch
    {
        JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count() && left.EnumerateObject().All(p => right.TryGetProperty(p.Name, out var v) && Same(p.Value, v)),
        JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() && left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Same(pair.First, pair.Second)),
        JsonValueKind.String => left.GetString() == right.GetString(), JsonValueKind.Number => left.GetDouble() == right.GetDouble(), _ => true,
    };
    private sealed class ScenarioHandler(JsonElement row) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public byte[]? Body { get; private set; }
        public string? Method { get; private set; }
        public string? ContentType { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken); Method = request.Method.Method; ContentType = request.Content.Headers.ContentType!.ToString();
            string failure = Text(row, "failure");
            if (failure.Length != 0) throw failure == "cancel" ? new OperationCanceledException() : new HttpRequestException("fixture-secret");
            byte[] bytes;
            if (row.TryGetProperty("responseBytes", out var encoded)) bytes = Convert.FromBase64String(encoded.GetString()!);
            else
            {
                string text = Text(row, "response", "{\"error\":null,\"result\":6}");
                if (row.TryGetProperty("responseSize", out var size)) { text = "{\"error\":null,\"result\":null}"; text += new string(' ', size.GetInt32() - text.Length); }
                bytes = Encoding.UTF8.GetBytes(text);
            }
            return new HttpResponseMessage((HttpStatusCode)AnkiConnectParityTests.Count(row, "status", 200)) { Content = new ByteArrayContent(bytes) };
        }
    }
}
