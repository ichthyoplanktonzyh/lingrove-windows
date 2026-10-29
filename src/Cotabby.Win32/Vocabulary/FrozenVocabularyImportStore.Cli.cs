using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
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
                "apply" => throw new CliException(CliErrorCode.Unavailable, "CLI mutations are not yet available."),
                _ => throw new CliException(CliErrorCode.InvalidInput, "Unknown command.")
            };
        }), cancellationToken);
    }
    private static string throwIfResourceNeeded(string command) => command is "resourceList" or "resourceGet"
        ? throw new CliException(CliErrorCode.InvalidInput, "A resource is required.") : "";
}
