using System.Text.Json.Nodes;
using Cotabby.Core.CLI;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>The app-owned CLI borrows the same canonical Store; implementations serialize
/// each request with notebook operations and return only acknowledged results.</summary>
public interface ICanonicalVocabularyCliStore
{
    Task<JsonObject> ExecuteCliAsync(CliRequest request, CancellationToken cancellationToken = default);
}
