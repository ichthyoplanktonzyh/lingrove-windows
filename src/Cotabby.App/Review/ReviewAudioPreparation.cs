using System.Collections.ObjectModel;
using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Review;

/// <summary>One preparation sheet owns its temporary recordings and borrows shared speech. UI
/// callers serialize actions on their context, which awaits retain like the source MainActor.
/// The sheet owns cancellation: discard stops audition and releases cached bytes, while an accepted
/// generation observes its caller token before publishing. No audio is persisted by this owner.</summary>
internal sealed class ReviewAudioPreparation(ISpeechExporting? speech) : INotifyPropertyChanged
{
    public Guid OwnerId { get; } = Guid.NewGuid();
    public ISpeechExporting? Speech { get; } = speech;
    private readonly Dictionary<ReviewAudioKind, ReviewPreparedAudio> _prepared = [];
    public IReadOnlyDictionary<ReviewAudioKind, ReviewPreparedAudio> Prepared => new ReadOnlyDictionary<ReviewAudioKind, ReviewPreparedAudio>(new Dictionary<ReviewAudioKind, ReviewPreparedAudio>(_prepared));
    public ReviewAudioKind? Generating { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public SpeechExportRequest Request(ReviewAudioKind kind, VocabularyEntry entry, ReviewCardContent content)
    {
        return Request(kind, entry.Language, content);
    }
    public SpeechExportRequest Request(ReviewAudioKind kind, Cotabby.Core.LanguageLearning.LearningLanguage? language, ReviewCardContent content)
    {
        if (Speech is null) throw new SpeechException(SpeechFailure.IncompleteConfiguration);
        return Speech.ExportRequest(kind == ReviewAudioKind.Expression ? content.Expression : content.Sentence,
            language, content.Sentence.Length == 0 ? null : content.Sentence);
    }
    public async Task<ReviewPreparedAudio> PrepareAsync(ReviewAudioKind kind, SpeechExportRequest request,
        bool force = false, CancellationToken cancellationToken = default, Action? beforeExport = null)
    {
        // Reuse precedes both the busy guard and cancellation in the original sheet owner.
        // Another slot's generation does not prevent borrowing a matching cached recording.
        if (!force && _prepared.TryGetValue(kind, out var current) && CanonicalStringComparer.Instance.Equals(current.Fingerprint, request.Fingerprint)) return current;
        if (Generating is not null) throw new SpeechException(SpeechFailure.OperationInProgress);
        Speech?.Stop(OwnerId); Generating = kind; Changed();
        try
        {
            if (Speech is null) throw new SpeechException(SpeechFailure.IncompleteConfiguration);
            beforeExport?.Invoke();
            var audio = await Speech.ExportAudioAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ReviewPreparedAudio(request.Fingerprint, audio);
            _prepared[kind] = result; Changed(); return result;
        }
        finally { Generating = null; Changed(); }
    }
    public async Task AuditionAsync(ReviewAudioKind kind, SpeechExportRequest request, CancellationToken cancellationToken = default)
    {
        var result = await PrepareAsync(kind, request, cancellationToken: cancellationToken);
        Speech?.Audition(result.Audio, OwnerId, request.Language);
    }
    public void Discard() { StopPreview(); _prepared.Clear(); Changed(); }
    public void StopPreview() => Speech?.Stop(OwnerId);
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
}
