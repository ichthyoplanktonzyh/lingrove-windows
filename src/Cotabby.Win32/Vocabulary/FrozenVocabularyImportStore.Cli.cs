using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore : ICanonicalVocabularyCliStore
{
    public Task<JsonObject> ExecuteCliAsync(CliRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Detach mutable batch before the caller can change it while awaiting the Store worker.
        var retained = CliWire.DecodeRequest(CliWire.Encode(request));
        return Run(() => Database.Access(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retained.Version != 1 || retained.ExpiresAt <= CliWire.Now())
                throw new CliException(CliErrorCode.Timeout, "Request expired or protocol version is unsupported.");
            var snapshot = LoadCurrent(cancellationToken);
            var search = new WindowsVocabularyTextSearch();
            string resource = retained.Resource ?? throwIfResourceNeeded(retained.Command);
            return retained.Command switch
            {
                "status" => CliVocabularyProjection.Status(snapshot.Document, snapshot.Revision),
                "list" => CliVocabularyProjection.List(snapshot.Document, snapshot.Revision, search, retained.Query, retained.Language, retained.Limit ?? 50, retained.Offset ?? 0),
                "get" => CliVocabularyProjection.Get(snapshot.Document, snapshot.Revision, retained.EntryId ?? throw new CliException(CliErrorCode.NotFound, "Entry not found.")),
                "resourceList" => CliResourceProjection.List(snapshot.Document, snapshot.Revision, resource, search, retained.Query, retained.RelatedId, retained.ClaimStatus, retained.Limit ?? 50, retained.Offset ?? 0),
                "resourceGet" => CliResourceProjection.Get(snapshot.Document, snapshot.Revision, resource, retained.ResourceId ?? throw new CliException(CliErrorCode.NotFound, "Resource not found.")),
                "apply" => ApplyCliResources(retained, snapshot, cancellationToken),
                _ => throw new CliException(CliErrorCode.InvalidInput, "Unknown command.")
            };
        }), cancellationToken);
    }
    private static string throwIfResourceNeeded(string command) => command is "resourceList" or "resourceGet"
        ? throw new CliException(CliErrorCode.InvalidInput, "A resource is required.") : "";

    // This internal checkpoint admits only resource operations. No client or production listener
    // is activated until the full add/append/update/capture and media planner is implemented.
    private JsonObject ApplyCliResources(CliRequest request, FrozenVocabularySnapshot before, CancellationToken cancellationToken)
    {
        var batch = CliInputContract.ValidateWire(request.Batch ?? throw new CliException(CliErrorCode.InvalidInput, "The apply command needs a batch."));
        var value = batch.CopyValue(); var key = value["idempotencyKey"]!.GetValue<string>();
        var fingerprint = CliIdentity.Fingerprint(batch);
        if (Database.Receipt(key) is { } receipt)
        {
            if (!StringComparer.Ordinal.Equals(receipt.Fingerprint, fingerprint)) throw new CliException(CliErrorCode.Conflict, "Idempotency key was already used for different content.");
            var replay = JsonNode.Parse(receipt.Result)?.AsObject() ?? throw new CliException(CliErrorCode.Io, "Invalid stored receipt.");
            replay["replayed"] = true; replay["currentRevision"] = before.Revision; return replay;
        }
        if (value["expectedRevision"] is { } expected && expected.GetValue<long>() != (long)before.Revision)
            throw new CliException(CliErrorCode.Conflict, "Notebook revision changed. Read resources again before writing.");
        if (!request.DryRun && before.Revision >= long.MaxValue) throw new CliException(CliErrorCode.Conflict, "Notebook revision cannot advance.");
        var draft = new CliResourceDraft(before.Document, _now()); var outcomes = new JsonArray();
        var operations = value["operations"]!.AsArray();
        for (int index = 0; index < operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = operations[index]!.AsObject().Single();
            if (operation.Key is not ("put" or "delete")) throw new CliException(CliErrorCode.Unavailable, "This CLI operation is not yet available.");
            var input = operation.Value!.AsObject(); var resource = input["resource"]!.GetValue<string>();
            var id = operation.Key == "put" ? draft.Put(resource, input["value"]!.AsObject()) : Guid.Parse(input["id"]!.GetValue<string>());
            if (operation.Key == "delete") draft.Delete(resource, id);
            outcomes.Add(new JsonObject { ["operation"] = index, ["resource"] = resource, ["id"] = id.ToString("D").ToUpperInvariant(), ["action"] = operation.Key });
        }
        var candidate = draft.Finish(); ValidateMediaContent(candidate);
        var existing = ImageReferences(before.Document); var references = ImageReferences(candidate); ValidateMediaBudget(references);
        foreach (var image in references.Where(image => !existing.Contains(image))) _ = ImageStorage.Read(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired before commit.");
        var result = new JsonObject { ["revision"] = before.Revision + (request.DryRun ? 0UL : 1UL), ["dryRun"] = request.DryRun, ["replayed"] = false, ["operations"] = outcomes };
        if (request.DryRun) return result;
        var retained = references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        var retired = new List<(VocabularyImageReference Image, VocabularyWindowsFile.FileIdentity Identity)>();
        foreach (var image in existing.Where(image => !retained.Contains(image.Filename)))
            if (CaptureCleanupIdentity(image) is { } identity) retired.Add((image, identity));
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired before commit.");
        var committed = CanonicalWritingPolicy.PrepareCommit(before.Document, candidate);
        var revision = Database.Write(committed, before.Document, before.Revision, new VocabularySQLiteReceipt(key, fingerprint, CliWire.Encode(result)), cancellationToken);
        foreach (var item in retired) RemoveOwnedImage(item.Image, item.Identity);
        _notifications.Writer.TryWrite(new(revision, committed));
        return result;
    }
}
