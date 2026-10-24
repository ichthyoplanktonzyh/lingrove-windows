using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Immutable correlation evidence for explicit detached work. These short IDs join
/// structured events; they are not cancellation leases and contain no editor/request text.</summary>
public sealed record LearningRequestContext(string RequestId, string OperationId, ModelRequestSource Source, string ModelName, string Engine);

/// <summary>Original req_ plus eight Crockford-base32 characters from five fresh UUID bytes.
/// No process-global counter or text hash joins unrelated user content into a diagnostic ID.</summary>
public static class LearningRequestIds
{
    public static string Generate()
    {
        byte[] bytes = Guid.NewGuid().ToByteArray(bigEndian: true); const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        ulong buffer = 0; int bits = 0; var output = new System.Text.StringBuilder("req_");
        for (int i = 0; i < 5; i++)
        {
            buffer = (buffer << 8) | bytes[i]; bits += 8;
            while (bits >= 5) { bits -= 5; output.Append(alphabet[(int)((buffer >> bits) & 31)]); }
        }
        return output.ToString();
    }
}
