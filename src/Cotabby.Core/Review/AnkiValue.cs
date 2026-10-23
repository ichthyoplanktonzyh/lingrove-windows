using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Review;

/// <summary>Closed, detached JSON values from the original Anki wire model. Static templates and
/// future transport share this boundary: booleans cannot masquerade as note IDs. Constructing or
/// encoding a value starts no connection and carries no application/runtime reference.</summary>
[JsonConverter(typeof(AnkiValueJsonConverter))]
public abstract record AnkiValue
{
    private AnkiValue() { }
    public IReadOnlyDictionary<string, AnkiValue>? Object => this is Map map ? map.Values : null;
    public IReadOnlyList<AnkiValue>? Array => this is Sequence sequence ? sequence.Values : null;
    public string? String => this is Text text ? text.Value : null;
    public long? Integer => this is Number { Value: var number } && double.IsFinite(number) && number > 0
        && number < (double)long.MaxValue && Math.Truncate(number) == number ? (long)number : null;
    public static AnkiValue Strings(IReadOnlyDictionary<string, string> values) => new Map(values.ToDictionary(pair => pair.Key, pair => (AnkiValue)new Text(pair.Value)));
    public static AnkiValue List(IReadOnlyList<string> values) => new Sequence(values.Select(value => (AnkiValue)new Text(value)).ToArray());
    public static AnkiValue Null { get; } = new NullValue();

    public sealed record Map : AnkiValue
    {
        public Map(IReadOnlyDictionary<string, AnkiValue> values) => Values = CollectionValues.Map(values);
        public IReadOnlyDictionary<string, AnkiValue> Values { get; }
        public bool Equals(Map? other) => other is not null && CollectionValues.MapEqual(Values, other.Values);
        public override int GetHashCode() => CollectionValues.MapHash(Values);
    }
    public sealed record Sequence : AnkiValue
    {
        public Sequence(IReadOnlyList<AnkiValue> values) => Values = CollectionValues.List(values);
        public IReadOnlyList<AnkiValue> Values { get; }
        public bool Equals(Sequence? other) => other is not null && CollectionValues.SequenceEqual(Values, other.Values);
        public override int GetHashCode() => CollectionValues.SequenceHash(Values);
    }
    public sealed record Text(string Value) : AnkiValue
    {
        public bool Equals(Text? other) => other is not null && CollectionValues.Same(Value, other.Value);
        public override int GetHashCode() => CollectionValues.StringHash(Value);
    }
    public sealed record Number(double Value) : AnkiValue
    {
        public bool Equals(Number? other) => other is not null && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
    public sealed record Boolean(bool Value) : AnkiValue;
    public sealed record NullValue : AnkiValue;
}

/// <summary>Materializes JSON into owned values before releasing the parser document. The
/// serializer caller still owns its byte/depth limits; no HTTP framing is implemented here.</summary>
public sealed class AnkiValueJsonConverter : JsonConverter<AnkiValue>
{
    public override bool HandleNull => true;
    public override AnkiValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    { using var document = JsonDocument.ParseValue(ref reader); return Materialize(document.RootElement); }
    private static AnkiValue Materialize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => AnkiValue.Null,
        JsonValueKind.True => new AnkiValue.Boolean(true), JsonValueKind.False => new AnkiValue.Boolean(false),
        JsonValueKind.String => new AnkiValue.Text(value.GetString()!),
        JsonValueKind.Number when value.TryGetDouble(out double number) && double.IsFinite(number) => new AnkiValue.Number(number),
        JsonValueKind.Array => new AnkiValue.Sequence(value.EnumerateArray().Select(Materialize).ToArray()),
        JsonValueKind.Object => Object(value),
        _ => throw new JsonException("Unsupported Anki JSON value."),
    };
    private static AnkiValue Object(JsonElement value)
    {
        var result = new Dictionary<string, AnkiValue>(CanonicalStringComparer.Instance);
        // Foundation decoding keeps the first occurrence, including canonically equivalent keys.
        foreach (var field in value.EnumerateObject()) if (!result.ContainsKey(field.Name)) result.Add(field.Name, Materialize(field.Value));
        return new AnkiValue.Map(result);
    }
    public override void Write(Utf8JsonWriter writer, AnkiValue value, JsonSerializerOptions options)
    {
        // JSONDecoder admits 512 containers, while JSONEncoder rejects the 512th even when
        // empty. Keep that observed boundary rather than inheriting the writer's 1000.
        bool container = value is AnkiValue.Map or AnkiValue.Sequence;
        if (writer.CurrentDepth >= 512 || container && writer.CurrentDepth >= 511)
            throw new JsonException("Anki JSON exceeds encoding depth.");
        switch (value)
        {
            case AnkiValue.NullValue: writer.WriteNullValue(); break;
            case AnkiValue.Boolean boolean: writer.WriteBooleanValue(boolean.Value); break;
            case AnkiValue.Number number: writer.WriteNumberValue(number.Value); break;
            case AnkiValue.Text text: writer.WriteStringValue(text.Value); break;
            case AnkiValue.Sequence sequence:
                writer.WriteStartArray(); foreach (var child in sequence.Values) Write(writer, child, options); writer.WriteEndArray(); break;
            case AnkiValue.Map map:
                writer.WriteStartObject(); foreach (var field in map.Values) { writer.WritePropertyName(field.Key); Write(writer, field.Value, options); } writer.WriteEndObject(); break;
            default: throw new JsonException("Unsupported Anki JSON value.");
        }
    }
}
