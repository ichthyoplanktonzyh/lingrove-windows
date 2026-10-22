namespace Cotabby.Core.LanguageLearning;

/// <summary>Only the GGUF metadata needed by the portable template policy crosses from Inference.</summary>
public sealed record LearningGgufMetadata(
    string Architecture,
    string? Name = null,
    string? ChatTemplate = null);

/// <summary>
/// Trusted framing and literal source are kept in distinct fields until native tokenization. This
/// prevents user text that resembles a model control token from creating a new instruction turn.
/// </summary>
public sealed record LearningPromptSegments(
    string TrustedPrefix,
    string LiteralBody,
    string TrustedSuffix)
{
    public string RenderedText => TrustedPrefix + LiteralBody + TrustedSuffix;
}

/// <summary>
/// Pure local-template policy. Inference reads bounded GGUF metadata and supplies it here; Core
/// resolves the same Mac template/family rules and returns framing without touching the model file.
/// </summary>
public static class LearningLocalPrompt
{
    /// <summary>
    /// Resolves the Mac-supported text chat format. A user's explicit template is authoritative and
    /// bypasses filename heuristics; Automatic uses architecture plus bounded metadata, retaining
    /// only the pinned Mac filename fallbacks for GGUFs that do not identify a variant themselves.
    /// </summary>
    public static LearningChatTemplate ResolveTemplate(
        LearningChatTemplate requestedTemplate,
        LearningGgufMetadata? metadata,
        string? modelFileName = null)
    {
        if (!Enum.IsDefined(requestedTemplate))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (requestedTemplate != LearningChatTemplate.Automatic) return requestedTemplate;
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Architecture))
            throw new LearningTranslationException(LearningTranslationFailure.UnsupportedLocalTemplate);

        return metadata.Architecture switch
        {
            "hunyuan-dense" => metadata.ChatTemplate?.Contains("<|extra_0|>", StringComparison.Ordinal) == true
                ? LearningChatTemplate.HyMt7
                : LearningChatTemplate.HyMt,
            "qwen35" or "qwen3" or "qwen3moe" => LearningChatTemplate.Qwen3,
            "qwen2" => Contains(modelFileName, "qwen3")
                ? LearningChatTemplate.Qwen3
                : LearningChatTemplate.ChatMl,
            "gemma4" => ResolveGemma4(modelFileName),
            "gemma3" => Contains(metadata.Name, "translategemma") || Contains(modelFileName, "translategemma")
                ? LearningChatTemplate.TranslateGemma
                : LearningChatTemplate.Gemma3,
            "gemma3n" or "gemma2" or "gemma" => LearningChatTemplate.Gemma3,
            "llama" => LearningChatTemplate.Llama3,
            _ => throw new LearningTranslationException(LearningTranslationFailure.UnsupportedLocalTemplate),
        };
    }

    /// <summary>
    /// Resolves the immutable settings snapshot used by one generation. The filename supplies
    /// metadata fallback only; model identity remains separate from the resolved task family.
    /// </summary>
    public static LearningLocalModelProfile ResolveProfile(
        LearningLocalModelConfiguration configuration,
        LearningGgufMetadata? metadata,
        string? modelFileName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration.ModelPath))
            throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);

        modelFileName ??= Path.GetFileName(configuration.ModelPath);
        var template = ResolveTemplate(configuration.RequestedTemplate, metadata, modelFileName);
        var family = LearningTranslationPrompt.ForTemplate(template);
        var sampling = LearningInstructionSampling.ForFamily(family);
        return new LearningLocalModelProfile(
            configuration.ModelPath,
            template,
            family,
            configuration.ContextWindowTokens,
            sampling,
            configuration.SamplingOverrides);
    }

    /// <summary>Maps a resolved template to the exact Mac delimiter pair for this model tokenizer.</summary>
    public static LearningPromptSegments GetSegments(LearningChatTemplate template, string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var (prefix, suffix) = Delimiters(template);
        return new LearningPromptSegments(prefix, prompt, suffix);
    }

    /// <summary>
    /// Validates actual token counts, including trusted framing. Specialists and context limits
    /// reject complete targets that do not fit; neither the sentence nor its prompt is clipped.
    /// </summary>
    public static void ValidateTokenBudget(
        int inputTokens,
        int outputTokens,
        LearningChatTemplate template,
        int contextWindowTokens)
    {
        if (!Enum.IsDefined(template))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (contextWindowTokens is < 1 or > ModelContextBudget.MaximumLocalTokens)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidContextBudget);
        if (template == LearningChatTemplate.TranslateGemma && inputTokens > 2_048)
            throw new LearningTranslationException(LearningTranslationFailure.TranslateGemmaInputTooLarge);
        if (inputTokens <= 0 || outputTokens <= 0 || inputTokens >= contextWindowTokens ||
            outputTokens >= contextWindowTokens - inputTokens)
        {
            throw ModelContextBudget.OversizedTarget();
        }
    }

    /// <summary>Trusted model control tokens; the literal body must use the model's ordinary text tokenizer.</summary>
    public static (string Prefix, string Suffix) Delimiters(LearningChatTemplate template) => template switch
    {
        LearningChatTemplate.HyMt => ("<｜hy_begin▁of▁sentence｜><｜hy_User｜>", "<｜hy_Assistant｜>"),
        LearningChatTemplate.HyMt7 => ("<|startoftext|>", "<|extra_0|>"),
        LearningChatTemplate.ChatMl => ("<|im_start|>user\n", "<|im_end|>\n<|im_start|>assistant\n"),
        // The empty thought block is part of the pinned Qwen3 instruction template; it prevents
        // hidden reasoning text from preceding the user-visible translation.
        LearningChatTemplate.Qwen3 => ("<|im_start|>user\n", "<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n"),
        LearningChatTemplate.Gemma4 => ("<bos><|turn>user\n", "<turn|>\n<|turn>model\n"),
        LearningChatTemplate.Gemma4Large => ("<bos><|turn>user\n", "<turn|>\n<|turn>model\n<|channel>thought\n<channel|>"),
        LearningChatTemplate.Gemma3 or LearningChatTemplate.TranslateGemma => ("<bos><start_of_turn>user\n", "<end_of_turn>\n<start_of_turn>model\n"),
        LearningChatTemplate.Llama3 => ("<|begin_of_text|><|start_header_id|>user<|end_header_id|>\n\n", "<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n"),
        LearningChatTemplate.Automatic => throw new LearningTranslationException(LearningTranslationFailure.UnsupportedLocalTemplate),
        _ => throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions),
    };

    private static LearningChatTemplate ResolveGemma4(string? modelFileName)
    {
        if (Contains(modelFileName, "e2b") || Contains(modelFileName, "e4b"))
            return LearningChatTemplate.Gemma4;
        throw new LearningTranslationException(LearningTranslationFailure.AmbiguousLocalTemplate);
    }

    private static bool Contains(string? value, string fragment) =>
        value?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
}
