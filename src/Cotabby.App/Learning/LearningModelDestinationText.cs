using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Learning;

/// <summary>Presentation of the same immutable destination used by generation. Validation
/// precedes rendering so invalid user-info/query URLs cannot disclose secrets in a card.
/// This helper performs no IO, credential access, provider activation or model discovery.</summary>
internal static class LearningModelDestinationText
{
    internal static string Translation(LearningTranslationBackend backend, string url, string model, bool draft)
    {
        string label;
        if (backend != LearningTranslationBackend.Endpoint)
            label = InterfaceText.Get("Local translation model") + ": " + (string.IsNullOrWhiteSpace(model) ? InterfaceText.Get("No model configured", "Learning") : ModelDisplayText.Safe(model));
        else
        {
            var endpoint = Endpoint(new() { Backend = LearningModelBackend.Endpoint, BaseUrl = url, EndpointModel = model });
            label = endpoint is null ? InterfaceText.Get("Translation endpoint is not configured correctly")
                : InterfaceText.Get("Translation endpoint") + ": " + endpoint.BaseUrl.AbsoluteUri + " · " + ModelDisplayText.Safe(endpoint.ModelName);
        }
        return label + (draft ? " · " + InterfaceText.Get("Draft reference enabled") : "");
    }

    /// <summary>Settings disclose the validated address even before a model is chosen. Only
    /// authored templates are localized; the destination and model stay detached content.</summary>
    internal static string TranslationSettings(string url, string model, bool enabled, bool draft)
    {
        try
        {
            var endpoint = new OpenAICompatibleEndpointConfiguration(url, model, OpenAICompatibleApiMode.ChatCompletions);
            string destination = InterfaceText.Format("Learning destination: {0} · {1}.", endpoint.BaseUrl.AbsoluteUri,
                string.IsNullOrWhiteSpace(model) ? InterfaceText.Get("No model configured", "Learning") : ModelDisplayText.Safe(endpoint.ModelName));
            string payload = InterfaceText.Get("Translation sends the target, optional draft excerpts and style instructions to the chosen engine. Alignment receives only the target and translation. Screens, clipboard and history are excluded.", "Learning");
            return string.Join(" ", destination, payload, Scope(endpoint),
                InterfaceText.Get(enabled ? "The endpoint is opted in for translation requests." : "Endpoint sending is off."))
                + (draft ? " · " + InterfaceText.Get("Draft reference enabled") : "");
        }
        catch (OpenAICompatibleEndpointException error)
        { return EndpointInterfaceText.Error(error.Error) + " " + InterfaceText.Get("Translation endpoint is not configured correctly"); }
    }
    internal static string Explanation(LearningModelSnapshot? profile, bool allowEmptyModel = false)
    {
        if (profile?.Backend != LearningModelBackend.Endpoint)
            return string.IsNullOrWhiteSpace(profile?.LocalModel)
                ? InterfaceText.Get("AI explanation model not configured")
                : InterfaceText.Get("Local AI explanation model") + ": " + ModelDisplayText.Safe(profile.LocalModel);
        return Endpoint(profile, requireModel: !allowEmptyModel) is { } endpoint
            ? InterfaceText.Get("AI explanation endpoint") + ": " + endpoint.BaseUrl.AbsoluteUri
                + " · " + (endpoint.ModelName.Length == 0 ? InterfaceText.Get("No model configured", "Learning") : ModelDisplayText.Safe(endpoint.ModelName))
                + "\n" + InterfaceText.Get("The selected text and its context are sent to this destination.") + "\n" + Scope(endpoint)
            : InterfaceText.Get("AI explanation endpoint is not configured correctly");
    }

    private static string Scope(OpenAICompatibleEndpointConfiguration endpoint) => endpoint.HostScope switch
    {
        OpenAICompatibleHostScope.Loopback => InterfaceText.Get("This endpoint is on this PC."),
        OpenAICompatibleHostScope.LocalNetwork => InterfaceText.Get("With this endpoint, this text is sent to the configured local-network service.", "Learning"),
        _ => InterfaceText.Get("With this endpoint, this text is sent to the configured HTTPS remote service.", "Learning"),
    };

    internal static OpenAICompatibleEndpointConfiguration? Endpoint(LearningModelSnapshot profile, bool requireModel = true)
    {
        if (profile.Backend != LearningModelBackend.Endpoint || requireModel && string.IsNullOrWhiteSpace(profile.EndpointModel)
            || profile.EndpointModel.Length > 1024 || profile.EndpointModel.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(profile.BaseUrl) || profile.BaseUrl.Length > 4096 || profile.BaseUrl.Any(char.IsControl)) return null;
        try { return new(profile.BaseUrl, profile.EndpointModel, OpenAICompatibleApiMode.ChatCompletions); }
        catch (OpenAICompatibleEndpointException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
