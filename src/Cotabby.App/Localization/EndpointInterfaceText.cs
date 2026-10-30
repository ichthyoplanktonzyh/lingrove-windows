using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Localization;

/// <summary>Only typed configuration and scope values become authored interface keys.
/// Remote failures, URLs, credentials and model identifiers never enter a catalog lookup.</summary>
internal static class EndpointInterfaceText
{
    internal static string Error(OpenAICompatibleEndpointError error) => InterfaceText.Get(error switch
    {
        OpenAICompatibleEndpointError.InvalidBaseUrl => "Enter an HTTP or HTTPS base URL without credentials, a query, or a fragment.",
        OpenAICompatibleEndpointError.InsecurePublicHttp => "Public endpoints must use HTTPS. HTTP is limited to this PC or the local network.",
        OpenAICompatibleEndpointError.EmptyModelName => "Choose or enter a model before generating suggestions.",
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    });
    internal static string? Privacy(OpenAICompatibleHostScope scope) => scope switch
    {
        OpenAICompatibleHostScope.Loopback => null,
        OpenAICompatibleHostScope.LocalNetwork => InterfaceText.Get("Lingrove will send typed text and any enabled context to this server on your local network.", "Learning"),
        _ => InterfaceText.Get("This server is outside your PC. Typed text and any enabled context will leave your device."),
    };
}
