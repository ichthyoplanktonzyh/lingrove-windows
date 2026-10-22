using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Explanation eligibility from the Mac model policy/settings boundary. Filename hints reject known
/// base/specialist models; local GGUF metadata must independently confirm a general instruction
/// format. This policy owns no settings, native allocation or model file, and performs no IO.
/// </summary>
public static class LearningExplanationModelPolicy
{
    public static bool SupportsGeneralInstructions(string model, LearningChatTemplate template)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enum.IsDefined(template) || IsKnownBaseModel(model)
            || template is LearningChatTemplate.HyMt or LearningChatTemplate.HyMt7 or LearningChatTemplate.TranslateGemma)
            return false;
        string normalized = string.Concat(new GraphemeString(model.ToLowerInvariant())
            .Where(character => character.IsLetter || character.IsNumber).Select(character => character.ToString()));
        return !normalized.Contains("translategemma", StringComparison.Ordinal)
            && !normalized.Contains("hymt", StringComparison.Ordinal)
            && !normalized.Contains("hunyuanmt", StringComparison.Ordinal);
    }

    /// <summary>
    /// Null means local metadata is not confirmed. Endpoints have no local metadata; explicit
    /// selection is still required by their future service's configuration check. An empty alias
    /// passing this capability rule alone never means the service is configured.
    /// </summary>
    public static bool? GeneralInstructionsAvailable(bool isLocal, string model, LearningChatTemplate selectedTemplate,
        LearningChatTemplate? resolvedTemplate = null)
    {
        if (!SupportsGeneralInstructions(model, selectedTemplate)) return false;
        if (!isLocal) return true;
        return resolvedTemplate is { } resolved ? SupportsGeneralInstructions(model, resolved) : null;
    }

    /// <summary>
    /// The future explanation operation calls this with metadata from its exclusive native lease,
    /// before prompt tokenization/generation. Always resolve Automatic for the independent evidence
    /// check, even when a user selected a manual framing template. This prevents a renamed specialist
    /// from acquiring a tutor prompt through a manual override. No cached file-path check can replace
    /// that operation-owned metadata; the selected manual framing remains authoritative after gating.
    /// </summary>
    public static LearningLocalModelProfile ResolveLocalProfile(LearningLocalModelConfiguration configuration,
        LearningGgufMetadata? metadata, string? modelFileName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Preserve the existing local-profile configuration failure before capability checking;
        // neither an empty nor blank path identifies an operation-owned model allocation.
        if (string.IsNullOrWhiteSpace(configuration.ModelPath))
            throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
        modelFileName ??= Path.GetFileName(configuration.ModelPath);
        if (string.IsNullOrEmpty(modelFileName) || !SupportsGeneralInstructions(modelFileName, configuration.RequestedTemplate))
            throw InstructionModelRequired();
        LearningChatTemplate resolved;
        try { resolved = LearningLocalPrompt.ResolveTemplate(LearningChatTemplate.Automatic, metadata, modelFileName); }
        catch (LearningTranslationException) { throw InstructionModelRequired(); }
        if (GeneralInstructionsAvailable(true, modelFileName, configuration.RequestedTemplate, resolved) != true)
            throw InstructionModelRequired();
        return LearningLocalPrompt.ResolveProfile(configuration, metadata, modelFileName);
    }

    private static bool IsKnownBaseModel(string filename)
    {
        string lower = filename.ToLowerInvariant();
        if (lower is "gemma-4-e2b.i1-q6_k.gguf" or "gemma-4-e4b.i1-q4_k_m.gguf") return true;
        var text = new GraphemeString(lower);
        return text.Split(character => !character.IsLetter && !character.IsNumber).Any(range => lower[range] == "base");
    }
    private static LearningExplanationException InstructionModelRequired() => new(LearningExplanationFailure.InstructionModelRequired);
}
