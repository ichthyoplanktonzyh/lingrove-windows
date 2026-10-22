namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Renders the existing generic, Hy-MT, and TranslateGemma prompt contracts. It owns only stable
/// text formatting and model-family selection; transport framing stays with the inference engine.
/// </summary>
public static class LearningTranslationPrompt
{
    public enum Family
    {
        Generic,
        HyMt,
        TranslateGemma,
    }

    /// <summary>Rendered text plus the transport and sampling facts the engine must preserve.</summary>
    public sealed record Request(string Prompt, bool UserOnly, LearningInstructionSampling Sampling);

    /// <summary>Whether a family can consume a style instruction and its tighter trained input cap.</summary>
    public static bool SupportsStyleInstructions(Family family) => family != Family.TranslateGemma;

    public static int MaximumInputTokens(Family family) => family == Family.TranslateGemma
        ? 2_048
        : int.MaxValue;

    /// <summary>
    /// Maps recognizable endpoint aliases to specialist templates. Opaque aliases keep the generic
    /// behavior instead of guessing a prompt family from a vague model name.
    /// </summary>
    public static Family ForEndpointModel(string endpointModel)
    {
        var normalized = new string((endpointModel ?? string.Empty)
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());
        if (normalized.Contains("translategemma", StringComparison.Ordinal)) return Family.TranslateGemma;
        if (normalized.Contains("hymt", StringComparison.Ordinal) ||
            normalized.Contains("hunyuanmt", StringComparison.Ordinal)) return Family.HyMt;
        return Family.Generic;
    }

    public static Request Render(
        string source,
        LearningLanguage sourceLanguage,
        LearningLanguage targetLanguage,
        Family family,
        LearningTranslationStyle? style = null)
    {
        if (source is null || !Enum.IsDefined(sourceLanguage) || !Enum.IsDefined(targetLanguage) ||
            !Enum.IsDefined(family))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        LearningTextScopePolicy.ValidateCompleteText(source);

        style ??= new LearningTranslationStyle();
        if (!Enum.IsDefined(style.Register))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        var instruction = style.Instruction;
        if (instruction is not null && !SupportsStyleInstructions(family))
            throw new LearningTranslationException(LearningTranslationFailure.UnsupportedStyle);

        // Serialize user text as JSON data in the generic path. Relaxed escaping keeps the
        // macOS prompt's literal Unicode while JSON still protects quotes, backslashes and controls.
        var preference = instruction is null ? string.Empty : StylePreference(instruction);
        return family switch
        {
            Family.Generic => new Request(preference + GenericPrompt(source, sourceLanguage, targetLanguage),
                UserOnly: false, LearningInstructionSampling.Standard),
            Family.HyMt => new Request(preference + HyMtPrompt(source, targetLanguage),
                UserOnly: true, LearningInstructionSampling.HyMt),
            Family.TranslateGemma => new Request(preference + TranslateGemmaPrompt(source, sourceLanguage, targetLanguage),
                UserOnly: true, LearningInstructionSampling.TranslateGemma),
            _ => throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions),
        };
    }

    public static Request Render(
        LearningTranslationInput input,
        LearningLanguage sourceLanguage,
        LearningLanguage targetLanguage,
        Family family)
    {
        if (input is null) throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        return Render(input.Source, sourceLanguage, targetLanguage, family, input.Style);
    }

    private static string GenericPrompt(string source, LearningLanguage sourceLanguage, LearningLanguage targetLanguage) =>
        LearningExpressionPrompt.Translation(source, sourceLanguage, targetLanguage);

    private static string HyMtPrompt(string source, LearningLanguage targetLanguage)
    {
        var target = targetLanguage == LearningLanguage.Chinese ? "Chinese" : targetLanguage.Name();
        return $"Translate the following text into {target}. " +
            "Note that you should only output the translated result without any additional explanation:\n" + source;
    }

    private static string TranslateGemmaPrompt(string source, LearningLanguage sourceLanguage, LearningLanguage targetLanguage)
    {
        var from = TranslateGemmaLanguage(sourceLanguage);
        var to = TranslateGemmaLanguage(targetLanguage);
        return $"You are a professional {from.Name} ({from.Code}) to {to.Name} ({to.Code}) translator. " +
            $"Your goal is to accurately convey the meaning and nuances of the original {from.Name} text while adhering to {to.Name} grammar, vocabulary, and cultural sensitivities.\n" +
            $"Produce only the {to.Name} translation, without any additional explanations or commentary. " +
            $"Please translate the following {from.Name} text into {to.Name}:\n\n\n" + source;
    }

    private static (string Name, string Code) TranslateGemmaLanguage(LearningLanguage language) => language switch
    {
        LearningLanguage.Chinese => ("Chinese", "zh-Hans"),
        LearningLanguage.TraditionalChinese => ("Traditional Chinese", "zh-Hant"),
        _ => (language.Name(), language.Code()),
    };

    private static string StylePreference(string instruction) =>
        "Translation style preference (JSON): " + LearningExpressionPrompt.Json(instruction) + ". " +
        "Apply only the requested register. Preserve meaning, facts, negation, names and numbers. " +
        "Return only the target translation, with no commentary.\n";
}

/// <summary>Immutable generation sampling values belonging to one translation operation.</summary>
public sealed record LearningInstructionSampling(
    double Temperature,
    double TopP,
    int TopK,
    double RepetitionPenalty)
{
    public static LearningInstructionSampling Standard { get; } = new(0.3, 0.9, 40, 1);
    public static LearningInstructionSampling HyMt { get; } = new(0.7, 0.6, 20, 1.05);
    public static LearningInstructionSampling TranslateGemma { get; } = new(0.3, 0.95, 64, 1);
}
