using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Typed Mac row-leaf JSON boundary, independent of disk or SQLite. Table selects the payload
/// alternative; there is no extra type tag. Source field names, optional absence, enum dictionary
/// arrays, content union and 2001 date epoch stay explicit. The archive still reconstructs
/// and validates the complete graph before accepting these decoded values.
/// </summary>
public static class VocabularyPayloadCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Encode(VocabularyDatabasePayload payload)
    {
        try
        {
            byte[] bytes = payload switch
            {
                VocabularyDatabasePayload.Entry value => Serialize(value.Value), VocabularyDatabasePayload.Sense value => Serialize(value.Value),
                VocabularyDatabasePayload.Usage value => Serialize(value.Value), VocabularyDatabasePayload.Explanation value => Serialize(value.Value),
                VocabularyDatabasePayload.Block value => Serialize(value.Value), VocabularyDatabasePayload.Review value => Serialize(value.Value),
                VocabularyDatabasePayload.Binding value => Serialize(value.Value), VocabularyDatabasePayload.Template value => Serialize(value.Value),
                _ => throw Invalid(),
            };
            return bytes;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException or FormatException or KeyNotFoundException)
        { throw Invalid(); }
    }
    public static VocabularyDatabasePayload Decode(ReadOnlyMemory<byte> bytes, VocabularyDatabaseTable table)
    {
        try
        {
            bytes = Utf8Input(bytes);
            return table switch
            {
                VocabularyDatabaseTable.Entries => new VocabularyDatabasePayload.Entry(Deserialize<VocabularyEntry>(bytes)),
                VocabularyDatabaseTable.Senses => new VocabularyDatabasePayload.Sense(Deserialize<VocabularySense>(bytes)),
                VocabularyDatabaseTable.Usages => new VocabularyDatabasePayload.Usage(Deserialize<VocabularyExample>(bytes)),
                VocabularyDatabaseTable.SenseExplanations or VocabularyDatabaseTable.UsageExplanations => new VocabularyDatabasePayload.Explanation(Deserialize<LearningExplanationRecord>(bytes)),
                VocabularyDatabaseTable.Blocks => new VocabularyDatabasePayload.Block(Deserialize<VocabularyContentBlock>(bytes)),
                VocabularyDatabaseTable.Reviews => new VocabularyDatabasePayload.Review(Deserialize<VocabularyReview>(bytes)),
                VocabularyDatabaseTable.Bindings => new VocabularyDatabasePayload.Binding(Deserialize<AnkiNoteBinding>(bytes)),
                VocabularyDatabaseTable.Templates => new VocabularyDatabasePayload.Template(Deserialize<ReviewLayoutTemplate>(bytes)),
                _ => throw Invalid(),
            };
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException or FormatException or KeyNotFoundException)
        { throw Invalid(); }
    }
    private static ReadOnlyMemory<byte> Utf8Input(ReadOnlyMemory<byte> input)
    {
        // Foundation detects UTF-16/32 as well as UTF-8. Preserve the observed BOM precedence:
        // FF FE is consumed as UTF-16, so a UTF-32LE BOM fails, although unmarked UTF-32LE works.
        // This belongs to decoding; encoding always emits UTF-8 without a BOM.
        var bytes = input.Span;
        Encoding? encoding = null; int skip = 0;
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); skip = 4; }
        else if (bytes.Length >= 4)
        {
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0) encoding = new UTF32Encoding(true, false, true);
            else if (bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) encoding = new UTF32Encoding(false, false, true);
            else if (bytes[0] == 0 && bytes[2] == 0) encoding = new UnicodeEncoding(true, false, true);
            else if (bytes[1] == 0 && bytes[3] == 0) encoding = new UnicodeEncoding(false, false, true);
        }
        if (encoding is not null) return StrictUtf8.GetBytes(encoding.GetString(bytes[skip..]));
        // JsonDocument can defer string decoding. Validate every byte, including future fields.
        _ = StrictUtf8.GetCharCount(bytes[skip..]);
        return input[skip..];
    }
    private static byte[] Serialize<T>(T value)
    {
        if (value is null) throw Invalid();
        ValidateValue(value, typeof(T));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 512 });
        ValidateShape(document.RootElement, typeof(T));
        return bytes;
    }
    private static T Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 512 });
        if (!HasDuplicateKeys(document.RootElement))
        {
            ValidateShape(document.RootElement, typeof(T));
            return document.RootElement.Deserialize<T>(Options) ?? throw Invalid();
        }
        // Foundation's keyed containers use the first occurrence. .NET takes the last; preserve
        // the first recursively before materializing a model. Enum dictionaries are unkeyed
        // arrays and retain their separate last-assignment behavior.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 512 }))
            WriteFirst(document.RootElement, writer);
        using var normalized = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 512 });
        ValidateShape(normalized.RootElement, typeof(T));
        return normalized.RootElement.Deserialize<T>(Options) ?? throw Invalid();
    }
    /// <summary>The whole-notebook codec shares the same Codable leaf rules without projecting
    /// through relational rows (which intentionally strip child ownership). These internal calls
    /// retain strict UTF input, required fields, first duplicate keys and fixed decode failures.</summary>
    internal static byte[] EncodePortableValue<T>(T value)
    {
        try { return Serialize(value); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException or FormatException or KeyNotFoundException)
        { throw Invalid(); }
    }
    internal static T DecodePortableValue<T>(ReadOnlyMemory<byte> bytes)
    {
        try { return Deserialize<T>(Utf8Input(bytes)); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException or FormatException or KeyNotFoundException)
        { throw Invalid(); }
    }
    private static bool HasDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(HasDuplicateKeys);
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(CanonicalStringComparer.Instance);
        return value.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicateKeys(property.Value));
    }
    private static void WriteFirst(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject(); var names = new HashSet<string>(CanonicalStringComparer.Instance);
            foreach (var property in value.EnumerateObject())
                if (names.Add(property.Name)) { writer.WritePropertyName(property.Name); WriteFirst(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteFirst(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver(); resolver.Modifiers.Add(Configure);
        var result = new JsonSerializerOptions
        {
            TypeInfoResolver = resolver, PropertyNamingPolicy = new SourceNames(), DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectNullableAnnotations = true, MaxDepth = 512, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        result.Converters.Add(new ContentConverter()); result.Converters.Add(new EpochDateConverter()); result.Converters.Add(new UpperUuidConverter());
        result.Converters.Add(new FoundationDataConverter());
        result.Converters.Add(new FoundationInt64Converter());
        result.Converters.Add(new EnumDictionaryConverterFactory()); result.Converters.Add(new AudioSetConverter());
        // A broad enum factory would override LearningLanguage's existing code converter (en,
        // zh-Hans, ...). Only the content style needs a new camel-case adapter here.
        result.Converters.Add(new JsonStringEnumConverter<VocabularyTextStyle>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return result;
    }
    private static void Configure(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        var nullability = new NullabilityInfoContext();
        for (int index = info.Properties.Count - 1; index >= 0; index--)
        {
            var property = info.Properties[index];
            if (property.AttributeProvider is not PropertyInfo member) continue;
            if (Computed(info.Type, member.Name)) { info.Properties.RemoveAt(index); continue; }
            // Swift synthesized Codable requires nonoptional fields even when Swift/C# declares
            // a default. Optional constructor parameters cannot silently reset a truncated row.
            // System.Text.Json cannot mark getter-only constructor-bound members required.
            // ValidateShape checks those too, without making nullable constructor arguments required.
            property.IsRequired = property.Set is not null && Required(member, nullability);
        }
    }
    private static bool Required(PropertyInfo member, NullabilityInfoContext nullability) => Nullable.GetUnderlyingType(member.PropertyType) is null
        && (member.PropertyType.IsValueType || nullability.Create(member).ReadState == NullabilityState.NotNull);
    private static void ValidateShape(JsonElement value, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value.ValueKind == JsonValueKind.Null) throw new JsonException();
        if (type.IsEnum)
        {
            if (value.ValueKind != JsonValueKind.String) throw new JsonException();
            var parsed = value.Deserialize(type, Options);
            if (parsed is not Enum member || !Enum.IsDefined(type, parsed) || value.GetString() != WireName(member)) throw new JsonException();
            return;
        }
        var info = Options.GetTypeInfo(type);
        if (info.Kind == JsonTypeInfoKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
            var nullability = new NullabilityInfoContext();
            foreach (var property in info.Properties)
            {
                if (property.AttributeProvider is not PropertyInfo member) continue;
                bool required = Required(member, nullability);
                if (!value.TryGetProperty(property.Name, out var child) || child.ValueKind == JsonValueKind.Null)
                { if (required) throw new JsonException(); continue; }
                ValidateShape(child, property.PropertyType);
            }
        }
        else if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            if (value.ValueKind != JsonValueKind.Array) throw new JsonException();
            var element = type.IsArray ? type.GetElementType()! : type.GetInterfaces().Append(type)
                .First(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
            foreach (var child in value.EnumerateArray()) ValidateShape(child, element);
        }
        else if (info.Kind == JsonTypeInfoKind.Dictionary)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
            var element = type.GetGenericArguments()[1];
            foreach (var child in value.EnumerateObject()) ValidateShape(child.Value, element);
        }
    }
    private static string WireName(Enum value)
    {
        if (value is LearningLanguage language) return language.Code();
        string name = Enum.GetName(value.GetType(), value) ?? throw new JsonException();
        return value.GetType().GetField(name)!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
            ?? JsonNamingPolicy.CamelCase.ConvertName(name);
    }
    private static void ValidateValue(object value, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value is string text) { _ = StrictUtf8.GetByteCount(text); return; }
        if (value is Enum member) { if (!Enum.IsDefined(type, member)) throw new JsonException(); return; }
        if (value is VocabularyBlockContent.Text block)
        { ValidateValue(block.Value ?? throw new JsonException(), typeof(VocabularyTextContent)); return; }
        if (value is VocabularyBlockContent.Image image)
        {
            ValidateValue(image.Reference ?? throw new JsonException(), typeof(VocabularyImageReference));
            ValidateValue(image.Caption ?? throw new JsonException(), typeof(string)); return;
        }
        bool dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);
        var info = Options.GetTypeInfo(type);
        if (dictionary || info.Kind == JsonTypeInfoKind.Dictionary)
        {
            var arguments = type.GetGenericArguments();
            foreach (object pair in (IEnumerable)value)
            {
                var pairType = pair.GetType();
                ValidateValue(pairType.GetProperty("Key")!.GetValue(pair) ?? throw new JsonException(), arguments[0]);
                ValidateValue(pairType.GetProperty("Value")!.GetValue(pair) ?? throw new JsonException(), arguments[1]);
            }
        }
        else if (info.Kind == JsonTypeInfoKind.Enumerable || value is IReadOnlySet<ReviewAudioKind>)
        {
            Type element = type.IsArray ? type.GetElementType()! : type.GetInterfaces().Append(type)
                .First(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
            foreach (object? item in (IEnumerable)value) ValidateValue(item ?? throw new JsonException(), element);
        }
        else if (info.Kind == JsonTypeInfoKind.Object)
        {
            var nullability = new NullabilityInfoContext();
            foreach (var property in info.Properties)
            {
                var item = property.Get!(value);
                if (item is null)
                { if (property.AttributeProvider is PropertyInfo required && Required(required, nullability)) throw new JsonException(); continue; }
                ValidateValue(item, property.PropertyType);
            }
        }
    }
    private static bool Computed(Type type, string name) =>
        type == typeof(VocabularyEntry) && name is nameof(VocabularyEntry.Examples) or nameof(VocabularyEntry.Explanations)
        || type == typeof(VocabularySense) && name == nameof(VocabularySense.ReviewLayouts)
        || type == typeof(VocabularyMeaning) && name == nameof(VocabularyMeaning.Id)
        || type == typeof(VocabularyImageReference) && name == nameof(VocabularyImageReference.Filename)
        || type == typeof(VocabularyReview) && name is nameof(VocabularyReview.ActiveBinding) or nameof(VocabularyReview.NeedsBindingChoice)
        || type == typeof(ReviewLayout) && name is nameof(ReviewLayout.Parts) or nameof(ReviewLayout.CustomContent) or nameof(ReviewLayout.AudioKinds);
    private sealed class SourceNames : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name == nameof(VocabularyExample.SourceApplicationId) ? "appBundleIdentifier"
            : name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal) ? JsonNamingPolicy.CamelCase.ConvertName(name[..^2]) + "ID"
            : JsonNamingPolicy.CamelCase.ConvertName(name);
    }
    private sealed class UpperUuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetGuid();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
    }
    private sealed class FoundationDataConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            string text = reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException();
            // .NET base64 decoding ignores whitespace; Foundation's default Data strategy does
            // not. Reject it before decoding instead of silently broadening backup admission.
            foreach (char value in text)
                if (value is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=')) throw new JsonException();
            return Convert.FromBase64String(text);
        }
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) => writer.WriteBase64StringValue(value);
    }
    private sealed class FoundationInt64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException();
            if (reader.TryGetInt64(out long integer)) return integer;
            double number = reader.GetDouble();
            // Foundation accepts integral decimal/exponent spellings. Its small-number fallback
            // goes through Double; at 2^53 and above we must not round a neighbouring integer into
            // another identity/version. Parse that bounded decimal value before narrowing.
            if (double.IsFinite(number) && Math.Abs(number) < 9007199254740992d && Math.Truncate(number) == number) return (long)number;
            // The reference decoder's non-integer-spelling path first requires an exactly
            // representable Int64 Double. Int64.MaxValue.0 rounds to +2^63 and is rejected,
            // although the bare integer token takes the successful fast path above.
            if (!double.IsFinite(number) || Math.Truncate(number) != number || number < -9223372036854775808d || number >= 9223372036854775808d) throw new JsonException();
            // System.Decimal has fewer significant digits than Foundation Decimal and could round
            // a long fractional suffix into the next integer. Inspect the coefficient/scale instead;
            // the reference's large-number path truncates its decimal toward zero after the
            // Double range check. The source corpus records this Foundation-specific behavior.
            using var token = JsonDocument.ParseValue(ref reader);
            string raw = token.RootElement.GetRawText(); int exponentIndex = raw.IndexOfAny(['e', 'E']);
            string mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex]; int exponent = 0;
            if (exponentIndex >= 0 && !int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) throw new JsonException();
            int dot = mantissa.IndexOf('.'); long scale = (long)exponent - (dot < 0 ? 0 : mantissa.Length - dot - 1);
            bool negative = mantissa[0] == '-'; string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-').TrimStart('0');
            if (digits.Length == 0) return 0;
            if (scale < 0)
            {
                long discarded = -scale; if (discarded >= digits.Length) throw new JsonException();
                int retained = digits.Length - (int)discarded;
                digits = digits[..retained]; scale = 0;
            }
            if (digits.Length + scale > 19) throw new JsonException();
            ulong magnitude = ulong.Parse(digits, CultureInfo.InvariantCulture);
            for (int i = 0; i < scale; i++) magnitude *= 10;
            if (magnitude > (negative ? 9223372036854775808UL : long.MaxValue)) throw new JsonException();
            return negative ? magnitude == 9223372036854775808UL ? long.MinValue : -(long)magnitude : (long)magnitude;
        }
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
    private sealed class EpochDateConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly long Epoch = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            decimal ticks = decimal.Round(reader.GetDecimal() * TimeSpan.TicksPerSecond, 0, MidpointRounding.ToEven);
            return new DateTimeOffset(checked(Epoch + (long)ticks), TimeSpan.Zero);
        }
        // Decimal JSON retains all .NET ticks locally while remaining a numeric Foundation Date.
        // Mac decodes these numbers as Double; its sub-tick precision is a separate format limit.
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.UtcTicks - Epoch) / (decimal)TimeSpan.TicksPerSecond);
    }
    private sealed class ContentConverter : JsonConverter<VocabularyBlockContent>
    {
        public override VocabularyBlockContent Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
            var cases = root.EnumerateObject().Where(property => property.Name is "text" or "image").ToArray();
            if (cases.Length != 1) throw new JsonException();
            var value = cases[0].Value;
            var payload = value.GetProperty("_0");
            ValidateShape(payload, cases[0].Name == "text" ? typeof(VocabularyTextContent) : typeof(VocabularyImageReference));
            return cases[0].Name == "text" ? new VocabularyBlockContent.Text(payload.Deserialize<VocabularyTextContent>(options) ?? throw new JsonException())
                : new VocabularyBlockContent.Image(payload.Deserialize<VocabularyImageReference>(options) ?? throw new JsonException(), value.GetProperty("caption").GetString() ?? throw new JsonException());
        }
        public override void Write(Utf8JsonWriter writer, VocabularyBlockContent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            switch (value)
            {
                case VocabularyBlockContent.Text text:
                    if (text.Value is null) throw new JsonException();
                    writer.WritePropertyName("text"); writer.WriteStartObject(); writer.WritePropertyName("_0"); JsonSerializer.Serialize(writer, text.Value, options); break;
                case VocabularyBlockContent.Image image:
                    if (image.Reference is null || image.Caption is null) throw new JsonException();
                    writer.WritePropertyName("image"); writer.WriteStartObject(); writer.WritePropertyName("_0"); JsonSerializer.Serialize(writer, image.Reference, options);
                    writer.WriteString("caption", image.Caption); break;
                default: throw new JsonException();
            }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
    }
    private sealed class EnumDictionaryConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && type.GetGenericArguments()[0].IsEnum;
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) => (JsonConverter)Activator.CreateInstance(
            typeof(EnumDictionaryConverter<,>).MakeGenericType(type.GetGenericArguments()))!;
    }
    private sealed class AudioSetConverter : JsonConverter<IReadOnlySet<ReviewAudioKind>>
    {
        public override IReadOnlySet<ReviewAudioKind> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            var result = new HashSet<ReviewAudioKind>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                string? token = reader.GetString(); var value = JsonSerializer.Deserialize<ReviewAudioKind>(ref reader, options);
                if (!Enum.IsDefined(value) || token != WireName(value)) throw new JsonException(); result.Add(value);
            }
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException();
            return result;
        }
        public override void Write(Utf8JsonWriter writer, IReadOnlySet<ReviewAudioKind> values, JsonSerializerOptions options)
        {
            writer.WriteStartArray(); foreach (var value in values)
            { if (!Enum.IsDefined(value)) throw new JsonException(); JsonSerializer.Serialize(writer, value, options); }
            writer.WriteEndArray();
        }
    }
    private sealed class EnumDictionaryConverter<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>> where TKey : struct, Enum
    {
        public override IReadOnlyDictionary<TKey, TValue> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            var result = new Dictionary<TKey, TValue>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException();
                string? token = reader.GetString(); var key = JsonSerializer.Deserialize<TKey>(ref reader, options);
                if (!Enum.IsDefined(key) || token != WireName(key) || !reader.Read() || reader.TokenType == JsonTokenType.EndArray) throw new JsonException();
                var value = JsonSerializer.Deserialize<TValue>(ref reader, options); if (value is null) throw new JsonException();
                result[key] = value;
            }
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException();
            return result;
        }
        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray(); foreach (var pair in value)
            { if (!Enum.IsDefined(pair.Key) || pair.Value is null) throw new JsonException(); JsonSerializer.Serialize(writer, pair.Key, options); JsonSerializer.Serialize(writer, pair.Value, options); }
            writer.WriteEndArray();
        }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
}
