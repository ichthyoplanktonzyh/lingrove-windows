using System.Collections.Frozen;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Dictionary source identities and language eligibility from Mac 3b. An unknown entry
/// language remains selectable but does not make an imported book an automatic lookup target.</summary>
public readonly record struct DictionarySource(string RawValue)
{
    public static DictionarySource Bundled => new("bundled");
    public static DictionarySource System => new("system");
    public static DictionarySource Online => new("online-english");
    public static DictionarySource MDict(Guid id) => new("mdict:" + id.ToString("D").ToUpperInvariant());
    public static DictionarySource Website(Guid id) => new("website:" + id.ToString("D").ToUpperInvariant());
    public static DictionarySource? Parse(string? raw)
    {
        if (raw is "bundled" or "system" or "online-english") return new(raw);
        if (raw is null) return null;
        int split = raw.IndexOf(':');
        if (split <= 0 || !Guid.TryParseExact(raw[(split + 1)..], "D", out var id)) return null;
        return raw[..split] switch { "mdict" => MDict(id), "website" => Website(id), _ => null };
    }
    public string Kind => RawValue switch { "bundled" => "bundled", "system" => "system", "online-english" => "online",
        _ when RawValue?.StartsWith("mdict:", StringComparison.Ordinal) == true => "mdict",
        _ when RawValue?.StartsWith("website:", StringComparison.Ordinal) == true => "website", _ => "invalid" };
}

public sealed record DictionaryLanguagePair(LearningLanguage? Entry = null, LearningLanguage? Definition = null, bool? Multilingual = null)
{
    public bool Supports(LearningLanguage? language) => Entry is null || language is null || Family(Entry.Value) == Family(language.Value);
    private static string Family(LearningLanguage language) => language is LearningLanguage.Chinese or LearningLanguage.TraditionalChinese ? "zh" : language.Code();
}

public sealed record MDictLibraryDescriptor(Guid Id, string Name, string MdxPath, IReadOnlyList<string> ResourcePaths,
    string FolderPath, long EntryCount, Guid Revision, DictionaryLanguagePair? Languages = null)
{
    public DictionarySource Source => DictionarySource.MDict(Id);
}
public sealed record DictionaryWebsite(Guid Id, string Name, string Template, DictionaryLanguagePair? Languages = null)
{
    public DictionarySource Source => DictionarySource.Website(Id);
}
public sealed record DictionaryResource(byte[] Data, string MimeType);

public sealed class DictionaryConfiguration
{
    private static readonly DictionarySource[] Builtins = [DictionarySource.Bundled, DictionarySource.System, DictionarySource.Online];
    public DictionaryConfiguration(IEnumerable<DictionarySource>? order = null, IEnumerable<DictionarySource>? enabled = null,
        IEnumerable<MDictLibraryDescriptor>? libraries = null, IEnumerable<DictionaryWebsite>? websites = null,
        LearningLanguage? definitionLanguage = null)
    {
        Libraries = Array.AsReadOnly((libraries ?? []).Select(x => x with { ResourcePaths = Array.AsReadOnly(x.ResourcePaths.ToArray()) }).ToArray());
        Websites = Array.AsReadOnly((websites ?? []).ToArray());
        DefinitionLanguage = definitionLanguage;
        var sources = Builtins.Concat(Libraries.Select(x => x.Source)).Concat(Websites.Select(x => x.Source)).ToArray();
        var valid = sources.ToHashSet(); Order = Array.AsReadOnly((order ?? Builtins).Concat(sources).Where(valid.Contains).Distinct().ToArray());
        var active = (enabled ?? [DictionarySource.Bundled, DictionarySource.System]).Where(valid.Contains).ToHashSet();
        if (active.Count == 0) active.Add(DictionarySource.Bundled); Enabled = active.ToFrozenSet();
    }
    public IReadOnlyList<MDictLibraryDescriptor> Libraries { get; }
    public IReadOnlyList<DictionaryWebsite> Websites { get; }
    public IReadOnlyList<DictionarySource> Order { get; }
    public IReadOnlySet<DictionarySource> Enabled { get; }
    public LearningLanguage? DefinitionLanguage { get; }
    public string Name(DictionarySource source) => Libraries.FirstOrDefault(x => x.Source == source)?.Name
        ?? Websites.FirstOrDefault(x => x.Source == source)?.Name ?? (source.Kind switch
        { "bundled" => "Built-in expressions", "system" => "System Dictionary", "online" => "Online English dictionary", "mdict" => "MDict dictionary", "website" => "Lookup website", _ => "Dictionary" });
    public LearningLanguage PreferredDefinitionLanguage(string locale)
    {
        if (DefinitionLanguage is { } selected) return selected;
        string code = locale.Replace('_', '-').ToLowerInvariant();
        if (code.StartsWith("zh", StringComparison.Ordinal))
            return code.Contains("hant", StringComparison.Ordinal) || code.Split('-').Last() is "tw" or "hk" or "mo" ? LearningLanguage.TraditionalChinese : LearningLanguage.Chinese;
        return LearningLanguages.FromCode(code.Length >= 2 ? code[..2] : code) ?? LearningLanguage.English;
    }
    public DictionaryLanguagePair? Languages(DictionarySource source) => source.Kind switch
    {
        "bundled" => new(null, LearningLanguage.English, true), "online" => new(LearningLanguage.English, LearningLanguage.English),
        "mdict" => Libraries.FirstOrDefault(x => x.Source == source)?.Languages,
        "website" => Websites.FirstOrDefault(x => x.Source == source)?.Languages, _ => null,
    };
    public IReadOnlyList<DictionarySource> Sources(LearningLanguage? language) => Array.AsReadOnly(Order.Where(s => Enabled.Contains(s) && (Languages(s)?.Supports(language) ?? true)).ToArray());
    public IReadOnlyList<DictionarySource> AutomaticSources(LearningLanguage? language, LearningLanguage preferredDefinition) =>
        Sources(language).Where(s => s.Kind != "website" && !(language is null && s.Kind == "online")
            && (s.Kind != "mdict" || Languages(s) is { } pair && (pair.Entry is not null || pair.Multilingual == true)))
        .Select((source, index) => (source, index, definition: Languages(source)?.Definition))
        .OrderBy(x => x.definition is null ? 1 : x.definition == preferredDefinition ? 0 : 2).ThenBy(x => x.index)
        .Select(x => x.source).ToArray();
}
