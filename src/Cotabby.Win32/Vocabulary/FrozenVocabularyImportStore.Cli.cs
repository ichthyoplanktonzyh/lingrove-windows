using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using F = Cotabby.Core.Vocabulary.Frozen;

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

    // No production listener is activated until capture and the complete CLI lifecycle are ready.
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
        var prepared = new Dictionary<string, VocabularyPreparedImage>(StringComparer.Ordinal);
        var operations = value["operations"]!.AsArray();
        for (int index = 0; index < operations.Count; index++)
        {
            var operation = operations[index]!.AsObject(); var action = operation["add"] ?? operation["append"];
            if (action?["content"] is not JsonArray blocks) continue;
            for (int position = 0; position < blocks.Count; position++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (blocks[position]?["image"] is not JsonObject image) continue;
                if (image["path"] is not null || image["data"] is null) throw new CliException(CliErrorCode.InvalidInput, "Images must be supplied as bytes by the CLI.");
                prepared[$"{index}.{position}"] = ImageStorage.Prepare(Convert.FromBase64String(image["data"]!.GetValue<string>()), cancellationToken);
            }
        }
        var plan = CliBatchPlan.Create(batch, before.Document, before.Revision, fingerprint, _now(), prepared, cancellationToken);
        var candidate = plan.Document; ValidateMediaContent(candidate);
        var existing = ImageReferences(before.Document); var references = ImageReferences(candidate); ValidateMediaBudget(references);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired before commit.");
        var result = new JsonObject { ["revision"] = before.Revision + (request.DryRun ? 0UL : 1UL), ["dryRun"] = request.DryRun, ["replayed"] = false, ["operations"] = plan.Outcomes };
        if (request.DryRun) return result;
        var retained = references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        var retired = new List<(VocabularyImageReference Image, VocabularyWindowsFile.FileIdentity Identity)>();
        foreach (var image in existing.Where(image => !retained.Contains(image.Filename)))
            if (CaptureCleanupIdentity(image) is { } identity) retired.Add((image, identity));
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired before commit.");
        var written = new List<(VocabularyImageReference Image, VocabularyWindowsFile.FileIdentity Identity)>();
        F.VocabularyDocument committed; ulong revision;
        try
        {
            foreach (var image in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plan.Images.TryGetValue(image.Filename, out var supplied))
                {
                    if (OwnedImageStorage.WriteOwned(supplied.CopyBytes(), image) is { } identity) written.Add((image, identity));
                }
                else if (!existing.Contains(image)) _ = ImageStorage.Read(image);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (request.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired before commit.");
            committed = CanonicalWritingPolicy.PrepareCommit(before.Document, candidate);
            revision = Database.Write(committed, before.Document, before.Revision, new VocabularySQLiteReceipt(key, fingerprint, CliWire.Encode(result)), cancellationToken);
        }
        catch { foreach (var item in written) RemoveOwnedImage(item.Image, item.Identity); throw; }
        foreach (var item in retired) RemoveOwnedImage(item.Image, item.Identity);
        _notifications.Writer.TryWrite(new(revision, committed));
        return result;
    }
}
