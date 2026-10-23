using System.Text;
using Cotabby.Core.Speech;
using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>Pure text freshness for already acknowledged audio receipts. The sheet/controller
/// still verifies configuration, endpoint, profile and media existence; a matching text hash alone
/// neither generates audio nor authorizes a remote write.</summary>
public static partial class AnkiReviewPolicy
{
    public static bool CurrentAudio(ReviewPlan plan, ReviewCardContent content)
    {
        foreach (var kind in plan.AudioKinds ?? new HashSet<ReviewAudioKind>())
        {
            if (!Enum.IsDefined(kind)) return false;
            string text = kind == ReviewAudioKind.Expression ? content.Expression : content.Sentence;
            string hash = SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(SpeechTextPolicy.SanitizedText(text) ?? ""));
            if (plan.AudioReceipts is not { } receipts || !receipts.TryGetValue(kind, out var receipt)
                || receipt is null || !CollectionValues.Same(receipt.TextHash, hash)) return false;
        }
        return true;
    }
}
