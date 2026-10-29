using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Models;

/// <summary>
/// A model the app can run. Built-in models carry a download URL. A model the
/// user dropped into the models folder has none.
/// </summary>
public sealed record CotabbyModel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Short human description shown in pickers ("fastest", "recommended").</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>Hugging Face URL of the .gguf file, or empty for a model the user supplied.</summary>
    public required string DownloadUrl { get; init; }

    public bool IsCustom => DownloadUrl.Length == 0;

    /// <summary>Size of the served file, shown before a download starts.</summary>
    public required long ApproxSizeBytes { get; init; }

    /// <summary>Context window to allocate at load time (token count).</summary>
    public required int ContextLength { get; init; }

    /// <summary>The file name under the models directory. Built-in models take it from their URL.</summary>
    public string FileName
    {
        get => _fileName ?? Path.GetFileName(new Uri(DownloadUrl).AbsolutePath);
        init => _fileName = value;
    }

    private readonly string? _fileName;
}

/// <summary>
/// Built-in model list. It is code and not configuration because the list is short and the
/// prompting strategy depends on the model.
/// </summary>
public static class ModelCatalog
{
    private static string HfUrl(string repo, string file) =>
        $"https://huggingface.co/{repo}/resolve/main/{file}?download=true";

    public static IReadOnlyList<CotabbyModel> All { get; } =
    [
        // The base checkpoints upstream Cotabby ships. A base model continues text where an
        // instruct model answers it, and the prompt pipeline depends on that. ApproxSizeBytes
        // are the exact Content-Length values of the files.
        new CotabbyModel
        {
            Id = "tabby-2-mini",
            DisplayName = "tabby-2-mini",
            Subtitle = "A solid everyday balance · 1.3 GB",
            DownloadUrl = HfUrl("mradermacher/Qwen3.5-2B-Base-i1-GGUF", "Qwen3.5-2B-Base.i1-Q4_K_M.gguf"),
            ApproxSizeBytes = 1_274_397_056,
            ContextLength = 4096,
        },
        new CotabbyModel
        {
            Id = "tabby-2-nano",
            DisplayName = "tabby-2-nano",
            Subtitle = "Fastest, for older or low-memory PCs · 0.6 GB",
            DownloadUrl = HfUrl("mradermacher/Qwen3.5-0.8B-Base-i1-GGUF", "Qwen3.5-0.8B-Base.i1-Q6_K.gguf"),
            ApproxSizeBytes = 629_744_512,
            ContextLength = 4096,
        },
        new CotabbyModel
        {
            Id = "tabby-2-base",
            DisplayName = "tabby-2-base",
            Subtitle = "Higher-quality suggestions · 3.9 GB",
            DownloadUrl = HfUrl("mradermacher/gemma-4-E2B-i1-GGUF", "gemma-4-E2B.i1-Q6_K.gguf"),
            ApproxSizeBytes = 3_845_328_608,
            ContextLength = 4096,
        },
        new CotabbyModel
        {
            Id = "tabby-2-pro",
            DisplayName = "tabby-2-pro",
            Subtitle = "Best quality, needs 16 GB of memory · 5.3 GB",
            DownloadUrl = HfUrl("mradermacher/gemma-4-E4B-i1-GGUF", "gemma-4-E4B.i1-Q4_K_M.gguf"),
            ApproxSizeBytes = 5_335_274_240,
            ContextLength = 4096,
        },
    ];

    private const string CustomIdPrefix = "custom:";

    /// <summary>The built-in models plus every other GGUF file in the models folder.</summary>
    public static IReadOnlyList<CotabbyModel> Available()
    {
        var directory = DefaultLocalDirectory();
        if (!Directory.Exists(directory)) return All;

        var builtIn = All.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var custom = Directory.EnumerateFiles(directory, "*.gguf")
            .Where(path => !builtIn.Contains(Path.GetFileName(path)))
            // Learning specialists are stored in the same folder but are instruction-tuned task
            // models, not autocomplete base checkpoints. Keep the Mac catalog's exact-name
            // exclusion at discovery so custom and base models remain available.
            .Where(path => LearningRuntimeCatalog.IsCompletionCandidate(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Custom);
        return [.. All, .. custom];
    }

    public static CotabbyModel? FindById(string id) =>
        id.StartsWith(CustomIdPrefix, StringComparison.Ordinal)
            ? Available().FirstOrDefault(m => m.Id == id)
            : All.FirstOrDefault(m => m.Id == id);

    /// <summary>Completion discovery applies the same explicit task admission as execution.</summary>
    public static IReadOnlyList<CotabbyModel> Available(IReadOnlyDictionary<string, string[]> assignments) =>
        CandidatesFrom(Available(), assignments);

    public static IReadOnlyList<CotabbyModel> CandidatesFrom(IEnumerable<CotabbyModel> models, IReadOnlyDictionary<string, string[]> assignments) =>
        models.Where(model => LocalModelTaskPolicy.IsCandidate(model.FileName, LocalModelTask.Completion,
            LocalModelTaskAssignments.TasksFor(model.FileName, assignments))).ToArray();

    public static CotabbyModel? FindById(string id, IReadOnlyDictionary<string, string[]> assignments) =>
        Available(assignments).FirstOrDefault(model => MatchesId(id, model.Id));

    /// <summary>Built-in IDs are stable exact identifiers; custom IDs name Windows files.</summary>
    public static bool MatchesId(string retainedId, string candidateId) =>
        string.Equals(retainedId, candidateId,
            retainedId.StartsWith(CustomIdPrefix, StringComparison.Ordinal) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static CotabbyModel Custom(string path)
    {
        long bytes = new FileInfo(path).Length;
        return new CotabbyModel
        {
            Id = CustomIdPrefix + Path.GetFileName(path),
            DisplayName = Path.GetFileNameWithoutExtension(path),
            Subtitle = $"Your own model · {bytes / 1e9:0.0} GB",
            DownloadUrl = "",
            FileName = Path.GetFileName(path),
            ApproxSizeBytes = bytes,
            ContextLength = 4096,
        };
    }

    /// <summary>Environment variable that points the model cache at another folder.</summary>
    public const string DirectoryOverrideVariable = "COTABBY_MODELS_DIR";

    /// <summary>Where GGUF files are cached. Defaults to %LOCALAPPDATA%\Cotabby\models.</summary>
    public static string DefaultLocalDirectory() =>
        Environment.GetEnvironmentVariable(DirectoryOverrideVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cotabby", "models");
}
