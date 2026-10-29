using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.CLI;

/// <summary>Uncommitted transaction-scoped batch plan; all raw records remain detached and the
/// final graph is admitted once after all operations.</summary>
public sealed record CliBatchPlan(F.VocabularyDocument Document, JsonArray Outcomes, IReadOnlyDictionary<string, VocabularyPreparedImage> Images)
{
    public static CliBatchPlan Create(CliBatch batch, F.VocabularyDocument baseline, ulong revision, string fingerprint,
        F.SourceDate now, IReadOnlyDictionary<string, VocabularyPreparedImage> prepared, CancellationToken cancellationToken = default)
    {
        var value = batch.CopyValue(); var operations = value["operations"]!.AsArray(); var key = value["idempotencyKey"]!.GetValue<string>();
        if (value["expectedRevision"] is { } expected && expected.GetValue<ulong>() != revision)
            throw new CliException(CliErrorCode.Conflict, "Notebook revision changed. Read it again before writing.");
        var draft = new CliResourceDraft(baseline, now); var outcomes = new JsonArray(); var images = new Dictionary<string, VocabularyPreparedImage>(StringComparer.Ordinal);
        for (int index = 0; index < operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var operation = operations[index]!.AsObject().Single(); var input = operation.Value!.AsObject();
            if (operation.Key is "put" or "delete")
            {
                var resource = input["resource"]!.GetValue<string>();
                var id = operation.Key == "put" ? draft.Put(resource, input["value"]!.AsObject()) : Guid.Parse(input["id"]!.GetValue<string>());
                if (operation.Key == "delete") draft.Delete(resource, id);
                outcomes.Add(new JsonObject { ["operation"] = index, ["resource"] = resource, ["id"] = id.ToString("D").ToUpperInvariant(), ["action"] = operation.Key });
            }
            else if (operation.Key is "add" or "append" or "update")
            {
                outcomes.Add(CliTraditionalPlan.Apply(draft, operation.Key, input, index, key, fingerprint, revision, now, prepared));
                foreach (var pair in prepared.Where(pair => pair.Key.StartsWith(index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", StringComparison.Ordinal))) images[pair.Value.Reference.Filename] = pair.Value;
            }
            else if (operation.Key == "capture")
                outcomes.Add(CliCapturePlan.Apply(draft, input, now, suffix => CliIdentity.OperationId(key, fingerprint, index, suffix), index));
            else throw new CliException(CliErrorCode.InvalidInput, "Unknown CLI operation.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(draft.Finish(), outcomes, images);
    }
}
