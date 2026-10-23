using System.Text;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;

namespace Cotabby.App.Review;

/// <summary>Audio remains explicit plan intent. A sheet caches temporary recordings; Send reuses
/// only a matching receipt whose media still exists in the same Anki profile. Successful uploads
/// are saved immediately, so a later failed note acknowledgement can retry without deleting media
/// that an already-committed remote note may reference.</summary>
internal sealed partial class AnkiReviewSession
{
    internal IReadOnlyDictionary<ReviewField, string> AudioFieldsForPreview => AnkiReviewPolicy.AudioFields(Plan);
    internal void ValidateAudioMapping()
    {
        if (Plan.AudioKinds?.Contains(ReviewAudioKind.Sentence) == true && (Content is not { } content || content.Sentence.Length == 0))
            throw new AnkiReviewException("Choose a review example again.");
        foreach (var kind in Plan.AudioKinds ?? new HashSet<ReviewAudioKind>())
            if (CardId is null && !Mapping.ContainsKey(kind.Field())) throw new AnkiReviewException("Map the selected audio fields or update the Lingrove template.");
    }
    internal async Task PrepareAudioAsync(ReviewAudioKind kind, bool audition, bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Current is not { } current || Content is not { } content) throw Conflict();
            var request = Audio.Request(kind, current.Entry, content);
            if (force && Plan.AudioReceipts is { } existing)
            { var receipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt>(existing); receipts.Remove(kind); Plan = Plan with { AudioReceipts = receipts }; }
            _ = await Audio.PrepareAsync(kind, request, force, cancellationToken);
            if (audition) await Audio.AuditionAsync(kind, request, cancellationToken);
        }
        catch (Exception error) { if (error is not OperationCanceledException) { Message = error.Message; Changed(); } }
    }
    private async Task<IReadOnlyDictionary<ReviewField, string>> UploadAudioAsync(ReviewCardContent content, AnkiConnection connection,
        string profile, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ReviewField, string>();
        if (Current is not { } current) throw Conflict();
        foreach (var kind in Enum.GetValues<ReviewAudioKind>().Where(kind => Plan.AudioKinds?.Contains(kind) == true))
        {
            var request = Audio.Request(kind, current.Entry, content); ReviewAudioReceipt receipt;
            if (Plan.AudioReceipts?.GetValueOrDefault(kind) is { } saved && Same(saved.Fingerprint, request.Fingerprint)
                && Same(saved.Endpoint, connection.Endpoint) && Same(saved.Profile, profile)
                && Has(AnkiReviewController.Strings(await Controller.CallAsync("getMediaFilesNames", Params(("pattern", new AnkiValue.Text(saved.Filename))), connection, cancellationToken)), saved.Filename)) receipt = saved;
            else
            {
                var prepared = await Audio.PrepareAsync(kind, request, cancellationToken: cancellationToken);
                ValidateCurrent(content, connection);
                if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
                string filename = SpeechExportPolicy.Filename(prepared.Audio);
                var stored = await Controller.CallAsync("storeMediaFile", Params(("filename", new AnkiValue.Text(filename)),
                    ("data", new AnkiValue.Text(Convert.ToBase64String(prepared.Audio.CopyBytes())))), connection, cancellationToken);
                if (!Same(stored.String, filename) || !Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw AnkiReviewController.Invalid();
                receipt = new(request.Fingerprint, SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(request.Text)), connection.Endpoint, profile, filename);
                var receipts = Plan.AudioReceipts is { } old ? new Dictionary<ReviewAudioKind, ReviewAudioReceipt>(old) : [];
                receipts[kind] = receipt; Plan = Plan with { AudioReceipts = receipts };
                await SavePlanAsync(cancellationToken);
            }
            result[kind.Field()] = "[sound:" + receipt.Filename + "]";
        }
        return result;
    }
}
