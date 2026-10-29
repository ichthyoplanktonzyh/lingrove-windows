using Cotabby.App.Speech;

namespace Cotabby.App.Learning;

/// <summary>Every lazily recreated writing window borrows the same application speech owner.</summary>
internal sealed partial class LearningPreviewController
{
    private LearningSpeechController? _speechController;
    internal void AttachSpeech(LearningSpeechController speech)
    {
        _dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(speech);
        if (_speechController is not null && !ReferenceEquals(_speechController, speech))
            throw new InvalidOperationException("Speech is already attached.");
        _speechController = speech;
        _window?.AttachSpeech(speech);
    }
}
