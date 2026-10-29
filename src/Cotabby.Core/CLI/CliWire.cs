using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cotabby.Core.CLI;

/// <summary>One bounded request addressed to an app instance; the server alone owns notebook IO.</summary>
public sealed record CliRequest
{
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("id")] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonPropertyName("expiresAt")] public double ExpiresAt { get; init; }
    [JsonPropertyName("command")] public string Command { get; init; } = "status";
    [JsonPropertyName("query")] public string? Query { get; init; }
    [JsonPropertyName("language")] public string? Language { get; init; }
    [JsonPropertyName("limit")] public int? Limit { get; init; }
    [JsonPropertyName("offset")] public int? Offset { get; init; }
    [JsonPropertyName("entryID")] public Guid? EntryId { get; init; }
    [JsonPropertyName("resource")] public string? Resource { get; init; }
    [JsonPropertyName("resourceID")] public Guid? ResourceId { get; init; }
    [JsonPropertyName("relatedID")] public Guid? RelatedId { get; init; }
    [JsonPropertyName("claimStatus")] public string? ClaimStatus { get; init; }
    [JsonPropertyName("batch")] public JsonObject? Batch { get; init; }
    [JsonPropertyName("dryRun")] public bool DryRun { get; init; }
    public static CliRequest FromArguments(CliArguments args, double expiry) => new() { ExpiresAt = expiry, Command = args.Command switch
    { CliCommand.Status => "status", CliCommand.List => "list", CliCommand.Get => "get", CliCommand.Apply => "apply", CliCommand.ResourceList => "resourceList", CliCommand.ResourceGet => "resourceGet", _ => throw new ArgumentOutOfRangeException(nameof(args)) },
        Query = args.Query, Language = args.Language, Limit = args.Limit, Offset = args.Offset, EntryId = args.EntryId,
        Resource = args.Resource, ResourceId = args.ResourceId, RelatedId = args.RelatedId, ClaimStatus = args.ClaimStatus, DryRun = args.DryRun };
}
public sealed record CliReply
{
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("requestID")] public required Guid RequestId { get; init; }
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("result")] public JsonNode? Result { get; init; }
    [JsonPropertyName("error")] public CliReplyError? Error { get; init; }
}
public sealed record CliReplyError([property: JsonPropertyName("code")] string Code, [property: JsonPropertyName("message")] string Message);

public static class CliWire
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 128,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new SourceGuidConverter() } };
    private sealed class SourceGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String && Guid.TryParseExact(reader.GetString(), "D", out var id) ? id : throw new JsonException();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
    }
    public static double Now() => (DateTimeOffset.UtcNow - new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
    public static byte[] Encode<T>(T value)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (data.Length > CliInputContract.MaximumBytes) throw new CliException(CliErrorCode.InvalidInput, "Response or request exceeds 32 MiB."); return data;
    }
    public static T Decode<T>(ReadOnlySpan<byte> value)
    {
        if (value.Length > CliInputContract.MaximumBytes) throw new CliException(CliErrorCode.InvalidInput, "Response or request exceeds 32 MiB.");
        try { return JsonSerializer.Deserialize<T>(value, Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new CliException(CliErrorCode.InvalidInput, "Invalid CLI request JSON."); }
    }
    public static CliRequest DecodeRequest(ReadOnlyMemory<byte> data)
    {
        if (data.Length > CliInputContract.MaximumBytes) throw new CliException(CliErrorCode.InvalidInput, "Response or request exceeds 32 MiB.");
        try
        {
            using var json = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 128 }); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out _) || !root.TryGetProperty("id", out _)
                || !root.TryGetProperty("expiresAt", out _) || !root.TryGetProperty("command", out _) || !root.TryGetProperty("dryRun", out _)) throw new JsonException();
            var request = JsonSerializer.Deserialize<CliRequest>(data.Span, Options) ?? throw new JsonException();
            if (!double.IsFinite(request.ExpiresAt) || request.Command is not ("status" or "list" or "get" or "apply" or "resourceList" or "resourceGet")) throw new JsonException();
            return request;
        }
        catch (JsonException) { throw new CliException(CliErrorCode.InvalidInput, "Invalid CLI request JSON."); }
    }
    public static CliReply DecodeReply(ReadOnlyMemory<byte> data)
    {
        if (data.Length > CliInputContract.MaximumBytes) throw new CliException(CliErrorCode.Io, "Invalid CLI response.");
        try
        {
            using var json = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 128 }); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out _) || !root.TryGetProperty("requestID", out _) || !root.TryGetProperty("ok", out _)) throw new JsonException();
            return JsonSerializer.Deserialize<CliReply>(data.Span, Options) ?? throw new JsonException();
        }
        catch (JsonException) { throw new CliException(CliErrorCode.Io, "Invalid CLI response."); }
    }
    public static CliReply Failure(Guid id, CliException error) => new() { RequestId = id, Ok = false, Error = new(Code(error.Code), error.Message) };
    public static string Code(CliErrorCode error) => error switch { CliErrorCode.InvalidInput => "invalid_input", CliErrorCode.NotFound => "not_found", CliErrorCode.Conflict => "conflict", CliErrorCode.Busy => "busy", CliErrorCode.Unavailable => "unavailable", CliErrorCode.Io => "io", CliErrorCode.Timeout => "timeout", _ => "io" };
}
