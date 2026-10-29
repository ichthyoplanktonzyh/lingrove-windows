using Cotabby.App.Localization;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Learning;

/// <summary>Presentation of the same immutable destination used by generation. Validation
/// precedes rendering so invalid user-info/query URLs cannot disclose secrets in a card.
/// This helper performs no IO, credential access, provider activation or model discovery.</summary>
internal static class LearningModelDestinationText
{
    internal static string Explanation(LearningModelSnapshot? profile)
    {
        if (profile?.Backend != LearningModelBackend.Endpoint)
            return string.IsNullOrWhiteSpace(profile?.LocalModel)
                ? InterfaceText.Get("AI explanation model not configured")
                : InterfaceText.Get("Local AI explanation model") + ": " + ModelDisplayText.Safe(profile.LocalModel);
        return Endpoint(profile) is { } endpoint
            ? InterfaceText.Get("AI explanation endpoint") + ": " + endpoint.BaseUrl.AbsoluteUri
                + " · " + ModelDisplayText.Safe(endpoint.ModelName)
                + "\n" + InterfaceText.Get("The selected text and its context are sent to this destination.")
            : InterfaceText.Get("AI explanation endpoint is not configured correctly");
    }

    internal static OpenAICompatibleEndpointConfiguration? Endpoint(LearningModelSnapshot profile)
    {
        if (profile.Backend != LearningModelBackend.Endpoint || string.IsNullOrWhiteSpace(profile.EndpointModel)
            || profile.EndpointModel.Length > 1024 || profile.EndpointModel.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(profile.BaseUrl) || profile.BaseUrl.Length > 4096 || profile.BaseUrl.Any(char.IsControl)) return null;
        try { return new(profile.BaseUrl, profile.EndpointModel, OpenAICompatibleApiMode.ChatCompletions); }
        catch (OpenAICompatibleEndpointException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
