using System.Text;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>Sheet-owned audio borrows the shared controller. Every asynchronous boundary keeps
/// the draft, speech configuration and destination current before a remote write or receipt.</summary>
internal sealed partial class CanonicalAnkiReviewSession
{
    private void ValidateAudioMapping(ReviewCardContent content)
    {
        RequireNoAudio();
        if (Plan.AudioKinds?.Contains(ReviewAudioKind.Sentence) == true && content.Sentence.Length == 0)
            throw new AnkiReviewException("Choose a review example again.");
        if (CardId is null && (Plan.AudioKinds ?? new HashSet<ReviewAudioKind>()).Any(kind => !Mapping.ContainsKey(kind.Field())))
            throw new AnkiReviewException("Map the selected audio fields or update the Lingrove template.");
    }
    internal async Task PrepareAudioAsync(ReviewAudioKind kind, bool audition, bool force = false, CancellationToken token = default)
    {
        try
        {
            var captured = CaptureSendState(); var configuration = Audio.Speech?.ExportConfigurationId;
            var current = await CurrentAsync(token); var content = CanonicalReviewCardPolicy.Content(current.Entry, current.Sense, captured.Plan);
            var request = Audio.Request(kind, current.Entry.Language, content);
            if (force && MatchesSendState(captured))
            {
                var receipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt>(Plan.AudioReceipts ?? new Dictionary<ReviewAudioKind, ReviewAudioReceipt>()); receipts.Remove(kind);
                Plan = Plan with { AudioReceipts = receipts }; captured = CaptureSendState();
            }
            var result = await Audio.PrepareAsync(kind, request, force, token);
            token.ThrowIfCancellationRequested();
            if (!MatchesSendState(captured) || configuration != Audio.Speech?.ExportConfigurationId) throw Conflict();
            var latest = await CurrentAsync(token);
            if (!MatchesSendState(captured) || configuration != Audio.Speech?.ExportConfigurationId || ReviewValue(latest.Sense, CardId) != _baseline
                || !Equals(CanonicalReviewCardPolicy.Content(latest.Entry, latest.Sense, captured.Plan), content)) throw Conflict();
            if (audition) Audio.Speech!.Audition(result.Audio, Audio.OwnerId, request.Language);
        }
        catch (Exception error) { if (error is not OperationCanceledException) { Message = error.Message; Changed(); } }
    }
    private async Task<IReadOnlyDictionary<ReviewField, string>> UploadAudioAsync(ReviewCardContent content, AnkiConnection connection, string profile, CancellationToken token)
    {
        var fields = new Dictionary<ReviewField, string>();
        foreach (var kind in Enum.GetValues<ReviewAudioKind>())
        {
            if (Plan.AudioKinds?.Contains(kind) != true) continue;
            var current = await CurrentAsync(token); await ValidateCurrentAsync(content, connection, token);
            var request = Audio.Request(kind, current.Entry.Language, content);
            ReviewAudioReceipt? receipt = Plan.AudioReceipts?.GetValueOrDefault(kind);
            bool reuse = false;
            if (receipt is not null && Same(receipt.Fingerprint, request.Fingerprint) && Same(receipt.Endpoint, connection.Endpoint) && Same(receipt.Profile, profile))
            {
                var names = AnkiReviewController.Strings(await Controller.CallAsync("getMediaFilesNames", Params(("pattern", new AnkiValue.Text(receipt.Filename))), connection, token));
                await ValidateCurrentAsync(content, connection, token); reuse = Has(names, receipt.Filename);
            }
            if (!reuse)
            {
                var prepared = await Audio.PrepareAsync(kind, request, cancellationToken: token);
                await ValidateCurrentAsync(content, connection, token);
                if (!Same(await Controller.ProfileAsync(connection, token), profile)) throw Conflict();
                await ValidateCurrentAsync(content, connection, token);
                string filename = SpeechExportPolicy.Filename(prepared.Audio);
                var stored = await Controller.CallAsync("storeMediaFile", Params(("filename", new AnkiValue.Text(filename)), ("data", new AnkiValue.Text(Convert.ToBase64String(prepared.Audio.CopyBytes())))), connection, token);
                if (!Same(stored.String, filename) || !Same(await Controller.ProfileAsync(connection, token), profile)) throw AnkiReviewController.Invalid();
                await ValidateCurrentAsync(content, connection, CancellationToken.None);
                receipt = new(request.Fingerprint, SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(request.Text)), connection.Endpoint, profile, filename);
                var receipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt>(Plan.AudioReceipts ?? new Dictionary<ReviewAudioKind, ReviewAudioReceipt>()) { [kind] = receipt };
                var updated = Plan with { AudioReceipts = receipts }; var expected = _sendState! with { Plan = updated };
                Plan = updated; _sendState = expected; RequireSendState();
                await SavePlanAsync(CancellationToken.None);
            }
            fields[kind.Field()] = "[sound:" + receipt!.Filename + "]";
        }
        return fields;
    }
}
