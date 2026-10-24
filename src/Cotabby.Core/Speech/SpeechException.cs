using System.Globalization;

namespace Cotabby.Core.Speech;

/// <summary>The original actionable speech failures, independent of native diagnostics or text.
/// Existing pure export admission retains its narrower two-case exception boundary.</summary>
public enum SpeechFailure
{
    IncompleteConfiguration, MissingLocalAssets, MismatchedLocalAssets, UnsupportedLanguage,
    UnrecognizedLanguage, RuntimeUnavailable, OperationInProgress, GenerationFailed, InvalidAudio,
    InvalidReferenceAudio, ResponseTooLarge, TextTooLong, EndpointStatus,
}

/// <summary>Fixed user-facing failure carrying only a category and optional HTTP status. Request
/// bodies, credentials, paths, endpoint responses and inner native errors never enter this value.</summary>
public sealed class SpeechException : Exception
{
    public SpeechException(SpeechFailure failure, int? status = null) : base(failure switch
    {
        SpeechFailure.IncompleteConfiguration => "Choose a speech model and voice in TTS settings first.",
        SpeechFailure.MissingLocalAssets => "Choose both the Qwen3-TTS model and its matching audio decoder.",
        SpeechFailure.MismatchedLocalAssets => "Choose the matching audio decoder for the selected speech model.",
        SpeechFailure.UnsupportedLanguage => "This local speech model does not support this language.",
        SpeechFailure.UnrecognizedLanguage => "Speech is not available for the detected language.",
        SpeechFailure.RuntimeUnavailable => "The local speech runtime is unavailable. Reinstall Lingrove and try again.",
        SpeechFailure.OperationInProgress => "Stop the current speech operation first.",
        SpeechFailure.GenerationFailed => "Speech generation failed. Check the model or connection and try again.",
        SpeechFailure.InvalidAudio => "The speech provider returned audio that could not be played.",
        SpeechFailure.InvalidReferenceAudio => "The reference voice file is unavailable. Choose a readable WAV or MP3 file.",
        SpeechFailure.ResponseTooLarge => "The speech audio exceeded the size limit. Try a shorter passage.",
        SpeechFailure.TextTooLong => "This passage is too long to read aloud. Choose a shorter passage.",
        // Foundation's localized integer interpolation groups digits. This English resource
        // snapshot retains its en_US formatting until the Windows localization layer owns it.
        SpeechFailure.EndpointStatus when status is not null => $"The speech server returned HTTP {status.Value.ToString("N0", CultureInfo.InvariantCulture)}. Check its TTS configuration.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    }) { Failure = failure; Status = failure == SpeechFailure.EndpointStatus ? status : null; }
    public SpeechFailure Failure { get; }
    public int? Status { get; }
}
