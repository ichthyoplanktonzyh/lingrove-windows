using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Localization;

/// <summary>Presentation of owned failure categories. Reconstruct source keys from the enum,
/// never from a foreign exception's message. Unknown OS/provider diagnostics remain literal.
/// Core keeps its original English contracts and fixture references; WPF owns localization.</summary>
internal static class InterfaceFailureText
{
    internal static string For(Exception error) => error switch
    {
        VocabularyExportException export => InterfaceText.Get(new VocabularyExportException(export.Failure).Message, "Vocabulary"),
        VocabularyException vocabulary => InterfaceText.Get(new VocabularyException(vocabulary.Failure).Message, "Vocabulary"),
        LearningTranslationException translation => InterfaceText.Get(new LearningTranslationException(translation.Failure).Message, "Learning"),
        LearningExplanationException explanation => InterfaceText.Get(new LearningExplanationException(explanation.Failure).Message,
            explanation.Failure == LearningExplanationFailure.InvalidContent ? "Vocabulary" : "Learning"),
        SpeechException speech when speech.Failure == SpeechFailure.EndpointStatus && speech.Status is { } status =>
            InterfaceText.FormatFrom("Learning", "The speech server returned HTTP %lld. Check its TTS configuration.", status.ToString("N0", InterfaceText.Culture)),
        SpeechException speech => InterfaceText.Get(new SpeechException(speech.Failure).Message, "Learning"),
        _ => error.Message,
    };

    // These two Core state fields explicitly contain only fixed policy messages. Match the
    // complete closed producer vocabulary; this is not an arbitrary string/exception lookup.
    // Unknown values stay raw, and source/result/clarification text never enters either method.
    private static readonly HashSet<string> TranslationKeys = new(
        Enum.GetValues<LearningTranslationFailure>().Select(failure => new LearningTranslationException(failure).Message), StringComparer.Ordinal)
    {
        "The translation engine is not ready. Check the translation settings.",
        "Translation failed. Check the translation settings and try again.",
    };
    private static readonly HashSet<string> ExplanationKeys = new(
        Enum.GetValues<LearningExplanationFailure>().Select(failure => new LearningExplanationException(failure).Message), StringComparer.Ordinal);
    private static readonly HashSet<string> DictionaryKeys = new(StringComparer.Ordinal)
    {
        "This dictionary reference is unavailable.", "The selected dictionary is unavailable.",
        "The local expression dictionary is unavailable.", "The local expression dictionary could not be read.",
        "This lookup text is unavailable.", "The dictionary could not complete this lookup.",
    };

    internal static string? TranslationState(string? message) => message is not null && TranslationKeys.Contains(message)
        ? InterfaceText.Get(message, "Learning") : message;
    internal static string? ExplanationState(string? message) => message is not null && ExplanationKeys.Contains(message)
        ? InterfaceText.Get(message, message == "This vocabulary item could not be verified." ? "Vocabulary" : "Learning")
        : TranslationState(message);
    internal static string? DictionaryState(string? message) => message is not null && DictionaryKeys.Contains(message)
        ? InterfaceText.Get(message, "Learning") : message;
}
