namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// One curated GGUF source. Downloads and user imports share the installed-model picker, while this
/// record preserves catalog provenance and validation metadata for the existing Get-model flow.
/// </summary>
public sealed record LearningDownloadableModel(
    string Filename,
    string DisplayName,
    Uri DownloadUrl,
    double ApproximateSizeInGigabytes,
    long? ExpectedSizeBytes = null,
    string? Sha256 = null,
    string? Recommendation = null)
{
    public string Id => DownloadUrl.AbsoluteUri;
}

/// <summary>
/// Task-specific learning model recommendations. Translation specialists stay out of the
/// autocomplete catalog, and context recommendations only apply to exact pinned catalog filenames.
/// </summary>
public static class LearningRuntimeCatalog
{
    private static readonly IReadOnlyList<LearningDownloadableModel> TranslationCatalog = Array.AsReadOnly(
    [
        new LearningDownloadableModel(
            "Hy-MT2-1.8B-Q4_K_M.gguf",
            "Hy-MT2 1.8B · Lightweight translation",
            new Uri("https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/a0c709d9fac510f2c807aa3af52872340dc37a4a/Hy-MT2-1.8B-Q4_K_M.gguf?download=true"),
            1.13,
            1_133_080_448,
            "dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699",
            "33 languages · Lightweight default for translating sentences after a pause."),
        new LearningDownloadableModel(
            "Hy-MT2-7B-Q4_K_M.gguf",
            "Hy-MT2 7B · Higher quality",
            new Uri("https://huggingface.co/tencent/Hy-MT2-7B-GGUF/resolve/ab8472660ac61fac25f1af43fac2599d52a8a775/Hy-MT2-7B-Q4_K_M.gguf?download=true"),
            4.62,
            4_624_648_896,
            "9f96256500f3fc1ab4d64336b58f52a949a95ad7516b0c229476eef782f9f77b",
            "33 languages · Larger translation model that requires more memory."),
        new LearningDownloadableModel(
            "translategemma-4b-it-Q4_K_M.gguf",
            "TranslateGemma 4B · Multilingual translation",
            new Uri("https://huggingface.co/bullerwins/translategemma-4b-it-GGUF/resolve/7c938465a870d8624bcfa98a8e4a3510053c19a8/translategemma-4b-it-Q4_K_M.gguf?download=true"),
            2.49,
            2_489_909_312,
            "7f7357c14abd9da4eb200b38b05da502cd6e10d7e1d403fbc9f78c19f3209b72",
            "55 languages · Google's translation model, distributed as a text-only GGUF quantization by bullerwins."),
    ]);

    private static readonly IReadOnlyList<LearningDownloadableModel> ExplanationCatalog = Array.AsReadOnly(
    [
        new LearningDownloadableModel(
            "Gemma-4-E4B-Q4_K_M.gguf",
            "Gemma-4-E4B-Q4_K_M",
            new Uri("https://registry.ollama.ai/v2/library/gemma4/blobs/sha256:370c2879f17648d337b0ab4e208fdce348e24d1060064e93d7645daafd56d39c"),
            5.49,
            5_493_439_296,
            "370c2879f17648d337b0ab4e208fdce348e24d1060064e93d7645daafd56d39c"),
        new LearningDownloadableModel(
            "Qwen3.5-4B-Q4_K_M.gguf",
            "Qwen3.5-4B-Q4_K_M",
            new Uri("https://registry.ollama.ai/v2/library/qwen3.5/blobs/sha256:a9d0a3e1d8c91732a3812becd31f8d4a8ff57414903f226306069f0437e2ac50"),
            2.65,
            2_648_593_216,
            "a9d0a3e1d8c91732a3812becd31f8d4a8ff57414903f226306069f0437e2ac50"),
    ]);

    public static IReadOnlyList<LearningDownloadableModel> TranslationModels => TranslationCatalog;
    public static IReadOnlyList<LearningDownloadableModel> ExplanationModels => ExplanationCatalog;

    /// <summary>Exact pinned filename recommendations; imports and aliases retain the 8,192 fallback.</summary>
    public static int? DefaultTranslationContextWindowTokens(string filename)
    {
        if (filename is null) return null;
        if (string.Equals(filename, "Hy-MT2-1.8B-Q4_K_M.gguf", StringComparison.OrdinalIgnoreCase)) return 8_192;
        if (string.Equals(filename, "translategemma-4b-it-Q4_K_M.gguf", StringComparison.OrdinalIgnoreCase)) return 4_096;
        return null;
    }

    /// <summary>Learning specialists cannot be selected as autocomplete base-completion checkpoints.</summary>
    public static bool IsCompletionCandidate(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        return !TranslationCatalog.Concat(ExplanationCatalog).Any(model =>
            string.Equals(model.Filename, filename, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<LearningDownloadableModel> ModelsFor(LearningModelRole role) => role switch
    {
        LearningModelRole.Translation => TranslationCatalog,
        LearningModelRole.Explanation => ExplanationCatalog,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown learning model role."),
    };
}
