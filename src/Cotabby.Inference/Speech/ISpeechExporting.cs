using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>Review borrows the shared speech controller through explicit actions. Implementations
/// and callers serialize UI-facing methods on their owner context. A fake can exercise preparation
/// without a synthesizer, device, model or endpoint; this interface itself owns none of them.</summary>
public interface ISpeechExporting
{
    string ExportConfigurationId { get; }
    SpeechExportRequest ExportRequest(string text, LearningLanguage language, string? context = null);
    Task<SpeechExportAudio> ExportAudioAsync(SpeechExportRequest request, CancellationToken cancellationToken = default);
    void Audition(SpeechExportAudio audio, Guid ownerId, LearningLanguage language);
    void Stop(Guid ownerId);
}
