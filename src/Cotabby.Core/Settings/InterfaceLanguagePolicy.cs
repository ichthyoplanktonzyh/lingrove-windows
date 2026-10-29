namespace Cotabby.Core.Settings;

/// <summary>Interface locale is independent of generation and the user's system language.
/// Unknown stored tags survive until the user explicitly chooses another value.</summary>
public static class InterfaceLanguagePolicy
{
    public static IReadOnlyList<(string Code, string Name)> Supported { get; } = Array.AsReadOnly(new[]
    {
        ("en", "English"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"), ("ja", "日本語"),
        ("ko", "한국어"), ("fr", "Français"), ("de", "Deutsch"), ("es", "Español"),
        ("pt-BR", "Português (Brasil)"), ("it", "Italiano"), ("ru", "Русский"),
        ("ar", "العربية"), ("hi", "हिन्दी"), ("id", "Bahasa Indonesia"), ("tr", "Türkçe")
    });

    /// <returns>A supported canonical tag, or null if the input cannot be safely normalized.</returns>
    public static string? Normalize(string identifier)
    {
        var pieces = identifier.Trim().Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToLowerInvariant()).ToArray();
        if (pieces.Length == 0) return null;
        if (pieces[0] == "zh")
        {
            if (pieces.Contains("hans")) return "zh-Hans";
            if (pieces.Contains("hant")) return "zh-Hant";
            if (pieces.Any(p => p is "cn" or "sg" or "my")) return "zh-Hans";
            if (pieces.Any(p => p is "tw" or "hk" or "mo")) return "zh-Hant";
            return null;
        }
        if (pieces[0] == "pt") return pieces.Contains("br") ? "pt-BR" : null;
        return Supported.FirstOrDefault(x => x.Code == pieces[0]).Code;
    }
}
