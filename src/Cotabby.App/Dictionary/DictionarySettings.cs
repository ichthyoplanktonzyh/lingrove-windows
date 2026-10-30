using System.IO;
using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Dictionaries;

/// <summary>One settings owner publishes complete immutable source snapshots. File descriptors
/// are references to user-selected files; editing the catalog never deletes or copies those files.</summary>
internal sealed class DictionarySettings(Func<AppSettings> current, Action persist)
{
    private sealed record Catalog(string[]? Order = null, string[]? Enabled = null, MDictLibraryDescriptor[]? Libraries = null,
        DictionaryWebsite[]? Websites = null, LearningLanguage? DefinitionLanguage = null);
    private DictionaryConfiguration _configuration = Read(current());
    private string? _raw = Raw(current());
    internal DictionaryConfiguration Configuration => _configuration;
    internal event Action<DictionaryConfiguration>? Changed;
    internal void Reload()
    {
        var settings = current(); var raw = Raw(settings); if (raw == _raw) return;
        _raw = raw; _configuration = Read(settings); Changed?.Invoke(_configuration);
    }
    internal bool SetEnabled(DictionarySource source, bool enabled)
    {
        if (!_configuration.Order.Contains(source)) return false;
        var next = _configuration.Enabled.ToHashSet();
        if (enabled) next.Add(source); else { if (next.Count <= 1) return false; next.Remove(source); }
        return Save(new(_configuration.Order, next, _configuration.Libraries, _configuration.Websites, _configuration.DefinitionLanguage));
    }
    internal bool Move(DictionarySource source, int delta)
    {
        var order = _configuration.Order.ToList(); int index = order.IndexOf(source), next = index + delta;
        if (index < 0 || next < 0 || next >= order.Count || Math.Abs(delta) != 1) return false;
        (order[index], order[next]) = (order[next], order[index]);
        return Save(new(order, _configuration.Enabled, _configuration.Libraries, _configuration.Websites, _configuration.DefinitionLanguage));
    }
    internal bool Add(MDictLibraryDescriptor library)
    {
        if (!Valid(library)) return false;
        var books = _configuration.Libraries.ToList();
        int index = books.FindIndex(x => x.Id == library.Id || string.Equals(x.MdxPath, library.MdxPath, StringComparison.OrdinalIgnoreCase));
        var enabled = _configuration.Enabled.ToHashSet();
        if (index >= 0)
        {
            var old = books[index]; books[index] = library with { Id = old.Id, Languages = library.Languages ?? old.Languages };
        }
        else { if (books.Count >= 32) return false; books.Add(library); enabled.Add(library.Source); }
        return Save(new(_configuration.Order, enabled, books, _configuration.Websites, _configuration.DefinitionLanguage));
    }
    internal bool SetLanguages(DictionarySource source, DictionaryLanguagePair pair)
    {
        if (!_configuration.Libraries.Any(x => x.Source == source)) return false;
        var books = _configuration.Libraries.Select(x => x.Source == source ? x with { Languages = pair } : x);
        return Save(new(_configuration.Order, _configuration.Enabled, books, _configuration.Websites, _configuration.DefinitionLanguage));
    }
    internal bool Remove(DictionarySource source)
    {
        if (source.Kind != "mdict") return false;
        return Save(new(_configuration.Order.Where(x => x != source), _configuration.Enabled.Where(x => x != source),
            _configuration.Libraries.Where(x => x.Source != source), _configuration.Websites, _configuration.DefinitionLanguage));
    }
    internal bool SetDefinitionLanguage(LearningLanguage? language) => Save(new(_configuration.Order, _configuration.Enabled,
        _configuration.Libraries, _configuration.Websites, language));
    private bool Save(DictionaryConfiguration value)
    {
        var catalog = new Catalog(value.Order.Select(x => x.RawValue).ToArray(), value.Order.Where(value.Enabled.Contains).Select(x => x.RawValue).ToArray(),
            value.Libraries.ToArray(), value.Websites.ToArray(), value.DefinitionLanguage);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog); if (bytes.Length > 2_000_000) return false;
        var settings = current(); var previous = settings.LearningDictionaryCatalogValue;
        settings.LearningDictionaryCatalogValue = JsonSerializer.SerializeToElement(catalog);
        try { persist(); } catch { settings.LearningDictionaryCatalogValue = previous; throw; }
        _raw = Raw(settings); _configuration = value; Changed?.Invoke(value); return true;
    }
    private static string? Raw(AppSettings settings) => settings.LearningDictionaryCatalogValue?.GetRawText();
    private static DictionaryConfiguration Read(AppSettings settings)
    {
        try
        {
            if (settings.LearningDictionaryCatalogValue is not { ValueKind: JsonValueKind.Object } value || System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > 2_000_000) return new();
            var catalog = value.Deserialize<Catalog>(); if (catalog is null) return new();
            var libraries = (catalog.Libraries ?? []).Where(Valid).Take(32).ToArray();
            var websites = (catalog.Websites ?? []).Where(x => x is not null && x.Id != Guid.Empty).Take(32).ToArray();
            return new(catalog.Order?.Select(DictionarySource.Parse).OfType<DictionarySource>(),
                catalog.Enabled?.Select(DictionarySource.Parse).OfType<DictionarySource>(), libraries, websites, catalog.DefinitionLanguage);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { return new(); }
    }
    private static bool Valid(MDictLibraryDescriptor? value) => value is not null && value.Id != Guid.Empty && value.Revision != Guid.Empty
        && !string.IsNullOrWhiteSpace(value.Name) && value.Name.Length <= 1000 && value.EntryCount >= 0 && value.EntryCount <= 20_000_000
        && value.ResourcePaths is not null && value.ResourcePaths.Count <= 16 && !string.IsNullOrWhiteSpace(value.MdxPath) && Path.IsPathFullyQualified(value.MdxPath)
        && !string.IsNullOrWhiteSpace(value.FolderPath) && Path.IsPathFullyQualified(value.FolderPath)
        && value.ResourcePaths.All(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path));
}
