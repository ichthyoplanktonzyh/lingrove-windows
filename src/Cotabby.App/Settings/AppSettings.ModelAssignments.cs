using System.Text.Json.Serialization;
using System.Text.Json;
using Cotabby.Core.Models;

namespace Cotabby.App.Settings;

/// <summary>Shared file-task assignments live in the existing settings owner, independent of runtimes.</summary>
public sealed partial class AppSettings
{
    public bool RecommendedSetupApplied { get; set; }
    public string? RecommendedCompletionTarget { get; set; }
    public string? RecommendedCompletionPrevious { get; set; }
    [JsonPropertyName("lingroveLocalModelTaskAssignments")]
    public Dictionary<string, string[]> LocalModelTaskAssignmentsValue { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("lingroveLocalModelSources")]
    public Dictionary<string, string> LocalModelSourcesValue { get; set; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string[]> ModelAssignmentSnapshot() =>
        (LocalModelTaskAssignmentsValue ?? []).ToDictionary(x => x.Key, x => x.Value?.ToArray() ?? [], StringComparer.Ordinal);

    public bool IsLocalModelCandidate(string filename, LocalModelTask task) =>
        LocalModelTaskPolicy.IsCandidate(filename, task, LocalModelTaskAssignments.TasksFor(filename, ModelAssignmentSnapshot()));

    public bool RecordLocalModelTask(string filename, LocalModelTask task)
    {
        if (string.IsNullOrEmpty(filename) || filename.IndexOfAny(['/', '\\', ':', '\0']) >= 0) return false;
        LocalModelTaskAssignmentsValue ??= new(StringComparer.Ordinal);
        return LocalModelTaskAssignments.TryRecord(filename, task, LocalModelTaskAssignmentsValue);
    }

    public void RecordLocalModelSource(string filename, Uri source)
    {
        LocalModelSourcesValue ??= new(StringComparer.Ordinal);
        LocalModelSourcesValue[filename.ToLowerInvariant()] = source.AbsoluteUri;
    }

    public void ClearLocalModelSource(string filename) => LocalModelSourcesValue?.Remove(filename.ToLowerInvariant());

    /// <summary>Windows protects the resident completion file as well as every retained task
    /// choice. Speech model and companion are kept even while that provider is inactive.</summary>
    public IReadOnlySet<string> ProtectedModelFilenames(string? residentCompletion = null)
    {
        static string? Leaf(JsonElement? value)
        {
            if (value is not { ValueKind: JsonValueKind.String } || value.Value.GetString() is not { Length: > 0 } path) return null;
            int separator = path.LastIndexOfAny(['/', '\\']);
            return path[(separator + 1)..];
        }
        return new HashSet<string>(ModelUsagePolicy.ProtectedFilenames(CompletionFilenameForId(ActiveModelId),
            [CompletionFilenameForId(BatteryModelId), CompletionFilenameForId(PluggedInModelId), residentCompletion,
             Leaf(SpeechLocalModelValue), Leaf(SpeechLocalProjectionValue)],
            [new[] { LearningLocalModel ?? "", LearningExplanationLocalModel ?? "" }]), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Register persisted explicit choices before discovery begins. Invalid cross-task choices stay stored but unavailable.</summary>
    public void NormalizeModelAssignmentsAndRegisterLegacy()
    {
        var assignments = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, values) in LocalModelTaskAssignmentsValue ?? [])
        {
            if (key is null || values is null) continue;
            var normalized = key.ToLowerInvariant();
            assignments[normalized] = (assignments.GetValueOrDefault(normalized) ?? []).Concat(values.Where(x => x is not null))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        LocalModelTaskAssignmentsValue = assignments;
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in LocalModelSourcesValue ?? []) if (key is not null && value is not null) sources[key.ToLowerInvariant()] = value;
        LocalModelSourcesValue = sources;
        foreach (var id in new[] { ActiveModelId, BatteryModelId, PluggedInModelId })
        {
            var filename = CompletionFilenameForId(id);
            if (filename is not null) RecordLocalModelTask(filename, LocalModelTask.Completion);
        }
        if (!string.IsNullOrEmpty(LearningLocalModel)) RecordLocalModelTask(LearningLocalModel, LocalModelTask.Translation);
        if (!string.IsNullOrEmpty(LearningExplanationLocalModel)) RecordLocalModelTask(LearningExplanationLocalModel, LocalModelTask.Explanation);
        if (SpeechLocalModelValue is { ValueKind: JsonValueKind.String } speech && speech.GetString() is { Length: > 0 } path)
        {
            int separator = path.LastIndexOfAny(['/', '\\']);
            var filename = path[(separator + 1)..];
            if (filename.Length > 0) RecordLocalModelTask(filename, LocalModelTask.Speech);
        }
    }

    public static string? CompletionFilenameForId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith("custom:", StringComparison.Ordinal))
        {
            var filename = id["custom:".Length..];
            return filename.Length > 0 && filename.IndexOfAny(['/', '\\', ':']) < 0 ? filename : null;
        }
        return ModelCatalog.All.FirstOrDefault(model => model.Id == id)?.FileName;
    }
}
