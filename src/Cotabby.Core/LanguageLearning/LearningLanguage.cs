using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// One durable translation direction identifier. The enum names are convenient in C#; the
/// <see cref="LearningLanguages.Code(LearningLanguage)"/> mapping is the stable stored value.
/// </summary>
[JsonConverter(typeof(LearningLanguageJsonConverter))]
public enum LearningLanguage
{
    English,
    Spanish,
    French,
    German,
    Italian,
    Portuguese,
    Dutch,
    Russian,
    Chinese,
    TraditionalChinese,
    Japanese,
    Korean,
    Hindi,
    Arabic,
}

/// <summary>Serializes the enum through its durable macOS-compatible language code.</summary>
public sealed class LearningLanguageJsonConverter : JsonConverter<LearningLanguage>
{
    public override LearningLanguage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var code = reader.GetString();
        var language = code is null ? null : LearningLanguages.FromCode(code);
        return language ?? throw new JsonException("The learning language code is not supported.");
    }

    public override void Write(Utf8JsonWriter writer, LearningLanguage value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Code());
}

/// <summary>Stable language codes and prompt-facing names shared by translation policy.</summary>
public static class LearningLanguages
{
    private static readonly IReadOnlyList<LearningLanguage> AllValues = Array.AsReadOnly(new[]
    {
        LearningLanguage.English,
        LearningLanguage.Spanish,
        LearningLanguage.French,
        LearningLanguage.German,
        LearningLanguage.Italian,
        LearningLanguage.Portuguese,
        LearningLanguage.Dutch,
        LearningLanguage.Russian,
        LearningLanguage.Chinese,
        LearningLanguage.TraditionalChinese,
        LearningLanguage.Japanese,
        LearningLanguage.Korean,
        LearningLanguage.Hindi,
        LearningLanguage.Arabic,
    });

    /// <summary>The complete stable palette, in the order used by the macOS reference.</summary>
    public static IReadOnlyList<LearningLanguage> All => AllValues;

    /// <summary>Returns the exact persisted/model code from the pinned macOS language catalog.</summary>
    public static string Code(this LearningLanguage language) => language switch
    {
        LearningLanguage.English => "en",
        LearningLanguage.Spanish => "es",
        LearningLanguage.French => "fr",
        LearningLanguage.German => "de",
        LearningLanguage.Italian => "it",
        LearningLanguage.Portuguese => "pt",
        LearningLanguage.Dutch => "nl",
        LearningLanguage.Russian => "ru",
        LearningLanguage.Chinese => "zh-Hans",
        LearningLanguage.TraditionalChinese => "zh-Hant",
        LearningLanguage.Japanese => "ja",
        LearningLanguage.Korean => "ko",
        LearningLanguage.Hindi => "hi",
        LearningLanguage.Arabic => "ar",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown learning language."),
    };

    /// <summary>Canonical English name placed in generic and specialist prompts.</summary>
    public static string Name(this LearningLanguage language) => language switch
    {
        LearningLanguage.English => "English",
        LearningLanguage.Spanish => "Spanish",
        LearningLanguage.French => "French",
        LearningLanguage.German => "German",
        LearningLanguage.Italian => "Italian",
        LearningLanguage.Portuguese => "Portuguese",
        LearningLanguage.Dutch => "Dutch",
        LearningLanguage.Russian => "Russian",
        LearningLanguage.Chinese => "Simplified Chinese",
        LearningLanguage.TraditionalChinese => "Traditional Chinese",
        LearningLanguage.Japanese => "Japanese",
        LearningLanguage.Korean => "Korean",
        LearningLanguage.Hindi => "Hindi",
        LearningLanguage.Arabic => "Arabic",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown learning language."),
    };

    /// <summary>Native-script picker label corresponding to the existing language palette.</summary>
    public static string Label(this LearningLanguage language) => language switch
    {
        LearningLanguage.English => "English",
        LearningLanguage.Spanish => "Español (Spanish)",
        LearningLanguage.French => "Français (French)",
        LearningLanguage.German => "Deutsch (German)",
        LearningLanguage.Italian => "Italiano (Italian)",
        LearningLanguage.Portuguese => "Português (Portuguese)",
        LearningLanguage.Dutch => "Nederlands (Dutch)",
        LearningLanguage.Russian => "Русский (Russian)",
        LearningLanguage.Chinese => "简体中文 (Simplified Chinese)",
        LearningLanguage.TraditionalChinese => "繁體中文 (Traditional Chinese)",
        LearningLanguage.Japanese => "日本語 (Japanese)",
        LearningLanguage.Korean => "한국어 (Korean)",
        LearningLanguage.Hindi => "हिन्दी (Hindi)",
        LearningLanguage.Arabic => "العربية (Arabic)",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown learning language."),
    };

    /// <summary>Resolves only one of the fourteen established codes; unsupported codes stay invalid.</summary>
    public static LearningLanguage? FromCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        foreach (var language in AllValues)
        {
            if (string.Equals(language.Code(), code, StringComparison.Ordinal)) return language;
        }

        return null;
    }
}
