using System.Text;
using Cotabby.Core.Support;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Validates a complete model response before it reaches the preview or an insertion boundary.
/// Stream prefixes remain bounded and safe to display, but only the completed response is eligible
/// for any later replacement transaction.
/// </summary>
public static class LearningExpressionOutputPolicy
{
    public const int MaximumOutputBytes = 12_000;

    /// <summary>Accumulation state owned by one engine request; it cannot grow beyond the full reply cap.</summary>
    public sealed class Stream
    {
        private readonly StringBuilder raw = new();
        private int utf8Bytes;

        /// <summary>Appends one already-decoded delta and returns its trimmed safe prefix, if any.</summary>
        public string? Append(string delta)
        {
            ArgumentNullException.ThrowIfNull(delta);
            var deltaBytes = Encoding.UTF8.GetByteCount(delta);
            if (deltaBytes > MaximumOutputBytes - utf8Bytes) throw InvalidOutput();

            raw.Append(delta);
            utf8Bytes += deltaBytes;
            var current = raw.ToString();
            if (string.IsNullOrWhiteSpace(current)) return null;
            return ValidatedText(current);
        }
    }

    /// <summary>Trims response edges, preserves internal newlines, and rejects unsafe whole output.</summary>
    public static string ValidatedText(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var text = raw.Trim();
        if (Encoding.UTF8.GetByteCount(text) > MaximumOutputBytes || !InsertionSafetyGate.IsSafeToInsert(text))
            throw InvalidOutput();
        return text;
    }

    private static LearningTranslationException InvalidOutput() =>
        new(LearningTranslationFailure.InvalidOutput);
}
