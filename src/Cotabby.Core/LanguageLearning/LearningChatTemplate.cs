using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Instruction formats supported by the learning runtimes. The learning task owns this catalog;
/// autocomplete uses a different base-continuation prompt and must not reuse these delimiters.
/// </summary>
[JsonConverter(typeof(LearningChatTemplateJsonConverter))]
public enum LearningChatTemplate
{
    Automatic,
    HyMt,
    HyMt7,
    ChatMl,
    Qwen3,
    Gemma4,
    Gemma4Large,
    Gemma3,
    TranslateGemma,
    Llama3,
}

/// <summary>The independent learning destination that owns a model selection and its settings.</summary>
public enum LearningModelRole
{
    Translation,
    Explanation,
}

/// <summary>Preserves the stable macOS Codable identifiers for the user's template choice.</summary>
public sealed class LearningChatTemplateJsonConverter : JsonConverter<LearningChatTemplate>
{
    public override LearningChatTemplate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "automatic" => LearningChatTemplate.Automatic,
            "hyMT" => LearningChatTemplate.HyMt,
            "hyMT7" => LearningChatTemplate.HyMt7,
            "chatML" => LearningChatTemplate.ChatMl,
            "qwen3" => LearningChatTemplate.Qwen3,
            "gemma4" => LearningChatTemplate.Gemma4,
            "gemma4Large" => LearningChatTemplate.Gemma4Large,
            "gemma3" => LearningChatTemplate.Gemma3,
            "translateGemma" => LearningChatTemplate.TranslateGemma,
            "llama3" => LearningChatTemplate.Llama3,
            _ => throw new JsonException("The learning chat template is not supported."),
        };

    public override void Write(Utf8JsonWriter writer, LearningChatTemplate value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            LearningChatTemplate.Automatic => "automatic",
            LearningChatTemplate.HyMt => "hyMT",
            LearningChatTemplate.HyMt7 => "hyMT7",
            LearningChatTemplate.ChatMl => "chatML",
            LearningChatTemplate.Qwen3 => "qwen3",
            LearningChatTemplate.Gemma4 => "gemma4",
            LearningChatTemplate.Gemma4Large => "gemma4Large",
            LearningChatTemplate.Gemma3 => "gemma3",
            LearningChatTemplate.TranslateGemma => "translateGemma",
            LearningChatTemplate.Llama3 => "llama3",
            _ => throw new JsonException("The learning chat template is not supported."),
        });
}

public static class LearningChatTemplatePolicy
{
    private static readonly IReadOnlyList<LearningChatTemplate> TranslationTemplates = Array.AsReadOnly(
        Enum.GetValues<LearningChatTemplate>());

    private static readonly IReadOnlyList<LearningChatTemplate> ExplanationTemplates = Array.AsReadOnly(
        Enum.GetValues<LearningChatTemplate>().Where(template =>
            template is not LearningChatTemplate.HyMt and
                not LearningChatTemplate.HyMt7 and
                not LearningChatTemplate.TranslateGemma).ToArray());

    /// <summary>
    /// Specialist translation adapters are not offered for explanation because their trained
    /// contracts do not provide a general explanation task. Stored legacy values are still read.
    /// </summary>
    public static IReadOnlyList<LearningChatTemplate> AvailableFor(LearningModelRole role) => role switch
    {
        LearningModelRole.Translation => TranslationTemplates,
        LearningModelRole.Explanation => ExplanationTemplates,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown learning model role."),
    };

    public static string Label(this LearningChatTemplate template) => template switch
    {
        LearningChatTemplate.Automatic => "Automatic",
        LearningChatTemplate.HyMt => "Hy-MT / Hy-MT2 1.8B",
        LearningChatTemplate.HyMt7 => "Hy-MT2 7B",
        LearningChatTemplate.ChatMl => "ChatML / Qwen2.5",
        LearningChatTemplate.Qwen3 => "Qwen3 / Qwen3.5 (thinking disabled)",
        LearningChatTemplate.Gemma4 => "Gemma 4 E2B / E4B (thinking disabled)",
        LearningChatTemplate.Gemma4Large => "Gemma 4 12B / 26B / 31B (thinking disabled)",
        LearningChatTemplate.Gemma3 => "Gemma 3",
        LearningChatTemplate.TranslateGemma => "TranslateGemma (translation only)",
        LearningChatTemplate.Llama3 => "Llama 3",
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown learning chat template."),
    };
}
