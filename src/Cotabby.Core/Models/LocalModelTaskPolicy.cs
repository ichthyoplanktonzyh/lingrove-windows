using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Models;

public enum LocalModelTask { Completion, Translation, Explanation, Speech }

public sealed record LocalModelDescriptor(string Filename, LocalModelTask Task, string Name, string Quantization, bool IsResource = false);

/// <summary>Frozen product task and alias metadata. Filename remains the file and settings identity.</summary>
public static class LocalModelCatalog
{
    public static IReadOnlyList<LocalModelDescriptor> Entries { get; } = Array.AsReadOnly<LocalModelDescriptor>(
    [
        new("Qwen3.5-0.8B-Base.i1-Q6_K.gguf", LocalModelTask.Completion, "Lingrove Nano", "Q6_K"),
        new("Qwen3.5-2B-Base.i1-Q4_K_M.gguf", LocalModelTask.Completion, "Lingrove Mini", "Q4_K_M"),
        new("gemma-4-E2B.i1-Q6_K.gguf", LocalModelTask.Completion, "Lingrove Base", "Q6_K"),
        new("gemma-4-E4B.i1-Q4_K_M.gguf", LocalModelTask.Completion, "Lingrove Pro", "Q4_K_M"),
        new("Hy-MT2-1.8B-Q4_K_M.gguf", LocalModelTask.Translation, "Lingrove Translate Nano", "Q4_K_M"),
        new("translategemma-4b-it-Q4_K_M.gguf", LocalModelTask.Translation, "Lingrove Translate Base", "Q4_K_M"),
        new("Hy-MT2-7B-Q4_K_M.gguf", LocalModelTask.Translation, "Lingrove Translate Pro", "Q4_K_M"),
        new("Qwen3.5-4B-Q4_K_M.gguf", LocalModelTask.Explanation, "Lingrove Explain Mini", "Q4_K_M"),
        new("Gemma-4-E4B-Q4_K_M.gguf", LocalModelTask.Explanation, "Lingrove Explain Base", "Q4_K_M"),
        new("Qwen3-TTS-12Hz-0.6B-Base.Q4_K_M.gguf", LocalModelTask.Speech, "Lingrove Voice Mini", "Q4_K_M"),
        new("Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf", LocalModelTask.Speech, "Lingrove Voice Base", "Q4_K_M"),
        new("mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf", LocalModelTask.Speech, "Lingrove Voice Base", "Q8_0", true),
        new("mmproj-Qwen3-TTS-12Hz-0.6B-Base-Q8_0.gguf", LocalModelTask.Speech, "Lingrove Voice Mini", "Q8_0", true),
        new("Qwen3-TTS-12Hz-0.6B-Base.mmproj-Q8_0.gguf", LocalModelTask.Speech, "Lingrove Voice Mini", "Q8_0", true),
    ]);

    public static LocalModelDescriptor? Descriptor(string filename) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.Filename, filename, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Task admission is independent of the runtime and requires explicit assignment for unknown files.</summary>
public static class LocalModelTaskPolicy
{
    public static bool Supports(string filename, LocalModelTask task)
    {
        ArgumentNullException.ThrowIfNull(filename);
        if (filename.Length == 0 || !Enum.IsDefined(task)) return false;
        if (LocalModelCatalog.Descriptor(filename) is { } entry) return entry.Task == task && !entry.IsResource;
        var lower = filename.ToLowerInvariant();
        var lowerCharacters = new GraphemeString(lower);
        var name = string.Concat(lowerCharacters.Where(character => character.IsLetterOrNumber).Select(character => character.ToString()));
        if (lowerCharacters.StartsWith("mmproj-") || ContainsCharacters(name, "mmproj")) return false;
        if (ContainsCharacters(name, "qwen3tts")) return task == LocalModelTask.Speech;
        if (ContainsCharacters(name, "translategemma") || ContainsCharacters(name, "hymt")
            || ContainsCharacters(name, "hunyuanmt")) return task == LocalModelTask.Translation;
        if (IsKnownBaseModel(lower)) return task == LocalModelTask.Completion;
        return true;
    }

    public static bool IsCandidate(string filename, LocalModelTask task, IReadOnlySet<LocalModelTask> assignedTasks) =>
        Supports(filename, task) && (LocalModelCatalog.Descriptor(filename) is not null || assignedTasks.Contains(task));

    public static bool PermitsTemplate(LearningChatTemplate template, LocalModelTask task) =>
        Enum.IsDefined(task) && (task != LocalModelTask.Explanation || template is not (LearningChatTemplate.HyMt or LearningChatTemplate.HyMt7 or LearningChatTemplate.TranslateGemma));

    private static bool IsKnownBaseModel(string lower)
    {
        if (lower is "gemma-4-e2b.i1-q6_k.gguf" or "gemma-4-e4b.i1-q4_k_m.gguf") return true;
        var characters = new GraphemeString(lower);
        return characters.Split(character => !character.IsLetterOrNumber)
            .Any(range => lower[range] == "base");
    }

    // Swift matches these ASCII family literals as Character sequences, not substrings inside
    // a combining cluster. Keep each retained grapheme intact when checking the literal.
    internal static bool ContainsCharacters(string text, string literal)
    {
        var characters = new GraphemeString(text);
        for (int start = 0; start <= characters.Count - literal.Length; start++)
        {
            int index = 0;
            while (index < literal.Length && characters[start + index].Is(literal[index])) index++;
            if (index == literal.Length) return true;
        }
        return false;
    }
}

/// <summary>Pure adapter over the shared settings map; its caller owns persistence and serialization.</summary>
public static class LocalModelTaskAssignments
{
    public static IReadOnlySet<LocalModelTask> TasksFor(string filename, IReadOnlyDictionary<string, string[]> values)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(values);
        var result = new HashSet<LocalModelTask>();
        if (values.TryGetValue(filename.ToLowerInvariant(), out var entries) && entries is not null)
            foreach (var entry in entries)
                if (ParseTask(entry) is { } task) result.Add(task);
        return result;
    }

    public static bool TryRecord(string filename, LocalModelTask task, IDictionary<string, string[]> values)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(values);
        if (!Enum.IsDefined(task)) return false;
        if (LocalModelCatalog.Descriptor(filename) is { } entry) return entry.Task == task;
        if (task == LocalModelTask.Speech && LocalModelTaskPolicy.ContainsCharacters(filename.ToLowerInvariant(), "mmproj")) return true;
        if (!LocalModelTaskPolicy.Supports(filename, task)) return false;
        var key = filename.ToLowerInvariant();
        var tasks = new HashSet<LocalModelTask>();
        if (values.TryGetValue(key, out var entries) && entries is not null)
            foreach (var value in entries) if (ParseTask(value) is { } parsed) tasks.Add(parsed);
        if (tasks.Add(task)) values[key] = tasks.Select(TaskName).Order(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static LocalModelTask? ParseTask(string? value) => value switch
    {
        "completion" => LocalModelTask.Completion, "translation" => LocalModelTask.Translation,
        "explanation" => LocalModelTask.Explanation, "speech" => LocalModelTask.Speech, _ => null,
    };
    private static string TaskName(LocalModelTask task) => task switch
    {
        LocalModelTask.Completion => "completion", LocalModelTask.Translation => "translation",
        LocalModelTask.Explanation => "explanation", LocalModelTask.Speech => "speech",
        _ => throw new ArgumentOutOfRangeException(nameof(task)),
    };
}

/// <summary>Snapshot of file names protected by current choices and future completion power profiles.</summary>
public static class ModelUsagePolicy
{
    public static IReadOnlySet<string> ProtectedFilenames(string? completion, IEnumerable<string?> completionPower,
        IEnumerable<IEnumerable<string>> learning)
    {
        var names = new HashSet<string>(learning.SelectMany(names => names), StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(completion)) names.Add(completion);
        foreach (var filename in completionPower) if (!string.IsNullOrEmpty(filename)) names.Add(filename);
        return names;
    }
}
