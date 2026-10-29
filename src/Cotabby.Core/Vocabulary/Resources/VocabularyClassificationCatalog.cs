using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Authored catalog manifest. Installation returns a new index only after validation.</summary>
public sealed record VocabularyClassificationCatalog
{
    private IReadOnlyList<VocabularyClassificationCatalogCategory> _categories = Array.Empty<VocabularyClassificationCatalogCategory>();
    private IReadOnlyList<VocabularyClassificationCatalogReference> _references = Array.Empty<VocabularyClassificationCatalogReference>();
    [JsonRequired] public required string Key { get; init; }
    [JsonRequired] public long Version { get; init; }
    [JsonRequired] public string Authorship { get; init; } = "";
    [JsonRequired] public IReadOnlyList<VocabularyClassificationCatalogReference> References { get => _references; init => _references = CollectionValues.List(value ?? throw new JsonException("Catalog references cannot be null.")); }
    [JsonRequired] public IReadOnlyList<VocabularyClassificationCatalogCategory> Categories { get => _categories; init => _categories = CollectionValues.List(value ?? throw new JsonException("Catalog categories cannot be null.")); }
    public const int MaximumCatalogBytes = 1_048_576;
    public const string ResourceName = "Cotabby.Core.Vocabulary.Resources.VocabularyClassificationCatalog.json";

    /// <summary>Only the versioned catalog manifest is decoded here, never an archive graph.</summary>
    public static VocabularyClassificationCatalog Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumCatalogBytes) throw VocabularyClassificationPolicy.Invalid();
        try
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, MaxDepth = 32 };
            options.Converters.Add(new ExactAxisConverter());
            options.Converters.Add(new VocabularyPayloadCodec.FoundationInt64Converter());
            var catalog = JsonSerializer.Deserialize<VocabularyClassificationCatalog>(bytes, options) ?? throw VocabularyClassificationPolicy.Invalid();
            if (!VocabularyClassificationPolicy.Fits(catalog.Authorship, 16_384) || catalog.References.Count > 256
                || catalog.References.Any(x => x is null || !VocabularyClassificationPolicy.Fits(x.Title, 4_096)
                    || !VocabularyClassificationPolicy.Fits(x.Url, 4_096) || !VocabularyClassificationPolicy.Fits(x.Role, 16_384))) throw VocabularyClassificationPolicy.Invalid();
            _ = catalog.Install(new VocabularyClassificationIndex(), []);
            return catalog;
        }
        catch (JsonException) { throw VocabularyClassificationPolicy.Invalid(); }
        catch (ArgumentException) { throw VocabularyClassificationPolicy.Invalid(); }
        catch (OverflowException) { throw VocabularyClassificationPolicy.Invalid(); }
        catch (FormatException) { throw VocabularyClassificationPolicy.Invalid(); }
    }
    // Swift Codable raw values are exact strings; System.Text.Json's stock enum converter
    // accepts case variants, which would admit a manifest the frozen Mac decoder rejects.
    private sealed class ExactAxisConverter : JsonConverter<VocabularyCategoryAxis>
    {
        public override VocabularyCategoryAxis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException();
            return reader.GetString() switch
            {
                "content" => VocabularyCategoryAxis.Content,
                "function" => VocabularyCategoryAxis.Function,
                _ => throw new JsonException(),
            };
        }
        public override void Write(Utf8JsonWriter writer, VocabularyCategoryAxis value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value switch
            {
                VocabularyCategoryAxis.Content => "content",
                VocabularyCategoryAxis.Function => "function",
                _ => throw new JsonException(),
            });
    }
    public static VocabularyClassificationCatalog Bundled()
    {
        using var stream = typeof(VocabularyClassificationCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new VocabularyException(VocabularyFailure.InvalidArchive);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bytes.Length + read > MaximumCatalogBytes) throw VocabularyClassificationPolicy.Invalid();
            bytes.Write(buffer, 0, read);
        }
        return Decode(bytes.ToArray());
    }
    public static Guid Identifier(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("lingrove.classification\0" + key));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }
    public (VocabularyClassificationIndex Index, bool Installed) Install(VocabularyClassificationIndex index, IReadOnlyList<VocabularyMeaningNode> nodes)
    {
        if (Version <= 0 || !VocabularyClassificationPolicy.Fits(Key, 200) || Key.Length == 0 || Categories.Count > 20_000 || Categories.Any(x => x is null || !VocabularyClassificationPolicy.Fits(x.Key, 200) || x.Key.Length == 0 || x.Parents.Count > 100_000)
            || Categories.Select(x => x.Key).Distinct(CanonicalStringComparer.Instance).Count() != Categories.Count) throw VocabularyClassificationPolicy.Invalid();
        var frameworkId = Identifier(Key);
        if (index.Frameworks.Any(x => x.Id == frameworkId && x.Version >= Version)) return (index, false);
        var keys = Categories.Select(x => x.Key).ToHashSet(CanonicalStringComparer.Instance);
        var oldIds = index.Categories.Where(x => x.FrameworkId == frameworkId).Select(x => x.Id).ToHashSet();
        var retired = index.Categories.Where(x => x.FrameworkId == frameworkId && !keys.Contains(x.Key ?? "")).Select(x => x with { IsRetired = true }).ToArray();
        var retiredIds = retired.Select(x => x.Id).ToHashSet();
        var categories = index.Categories.Where(x => x.FrameworkId != frameworkId).ToList();
        foreach (var category in Categories)
        {
            var seed = Key + "." + category.Key;
            categories.Add(new VocabularyCategory(Identifier(seed), category.Axis,
                [new VocabularyMeaningLabel(category.English) { Id = Identifier(seed + ".en"), Language = LearningLanguage.English },
                 new VocabularyMeaningLabel(category.Chinese) { Id = Identifier(seed + ".zh"), Language = LearningLanguage.Chinese }])
                { Definition = category.Definition, FrameworkId = frameworkId, Key = category.Key,
                  Provenance = new() { Origin = VocabularySemanticOrigin.Imported, Status = VocabularySemanticStatus.Confirmed, Source = $"Lingrove classification catalog {Version}" } });
        }
        categories.AddRange(retired);
        var parents = index.Parents.Where(x => !oldIds.Contains(x.CategoryId) || retiredIds.Contains(x.CategoryId)).ToList();
        foreach (var category in Categories) foreach (var parent in category.Parents)
        {
            if (!keys.Contains(parent)) throw VocabularyClassificationPolicy.Invalid();
            parents.Add(new(Identifier(Key + ".edge." + category.Key + "." + parent), Identifier(Key + "." + category.Key), Identifier(Key + "." + parent)));
        }
        var next = index with { Frameworks = index.Frameworks.Where(x => x.Id != frameworkId).Append(new(frameworkId, Key, Version)).ToArray(), Categories = categories, Parents = parents };
        VocabularyClassificationPolicy.Validate(next, nodes);
        return (next, true);
    }
}
public sealed record VocabularyClassificationCatalogReference(string Title, string Url, string Role);
public sealed record VocabularyClassificationCatalogCategory
{
    private IReadOnlyList<string> _parents = Array.Empty<string>();
    [JsonRequired] public required string Key { get; init; }
    [JsonRequired] public VocabularyCategoryAxis Axis { get; init; }
    [JsonRequired] public required string English { get; init; }
    [JsonRequired] public required string Chinese { get; init; }
    [JsonRequired] public string Definition { get; init; } = "";
    [JsonRequired] public IReadOnlyList<string> Parents { get => _parents; init => _parents = CollectionValues.List(value ?? throw new JsonException("Category parents cannot be null.")); }
}
