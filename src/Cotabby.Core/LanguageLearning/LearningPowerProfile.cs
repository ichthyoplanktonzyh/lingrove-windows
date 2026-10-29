namespace Cotabby.Core.LanguageLearning;

/// <summary>A task-owned power selection. Address and credential belong to the role, not this value.</summary>
public sealed record LearningPowerProfile
{
    public LearningModelBackend Backend { get; init; }
    public string LocalModel { get; init; } = "";
    public string EndpointModel { get; init; } = "";
    public LearningChatTemplate Template { get; init; } = LearningChatTemplate.Automatic;

    public static LearningPowerProfile Local(string model, LearningChatTemplate template = LearningChatTemplate.Automatic) =>
        new() { LocalModel = model ?? "", Template = template };
    public static LearningPowerProfile Endpoint(string model) =>
        new() { Backend = LearningModelBackend.Endpoint, EndpointModel = model ?? "" };
    public LearningPowerProfile ActiveProjection() => Backend switch
    {
        LearningModelBackend.Endpoint => Endpoint(EndpointModel ?? ""),
        LearningModelBackend.Local => Local(LocalModel ?? "", Enum.IsDefined(Template) ? Template : LearningChatTemplate.Automatic),
        _ => Local(""),
    };
}

public static class LearningPowerPolicy
{
    public static LearningPowerProfile Resolve(bool enabled, bool pluggedIn, LearningPowerProfile manual,
        LearningPowerProfile? battery, LearningPowerProfile? plugged) =>
        (enabled ? (pluggedIn ? plugged : battery) ?? manual : manual).ActiveProjection();
}
