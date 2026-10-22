using System.Text.Json;
using Cotabby.Core.Support;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Pure validation, bounded prompting and output parsing adapted from the Mac explanation policy.
/// Future card/runtime owners provide a frozen input and receive a prompt or complete result; this
/// boundary performs no capture, IO, generation, notebook writes or live editor reads.
/// </summary>
public static partial class LearningExplanationPolicy
{
    public static bool IsValid(LearningExplanationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (LearningExplanationText.Trim(input.Target).Length == 0
            || !LearningExplanationText.Fits(input.Target, 32_000)
            || !LearningExplanationText.Fits(input.DictionaryForm, 32_000)
            || !LearningExplanationText.Fits(input.Context, ModelContextBudget.MaximumTextBytes)
            || input.Ranges.Count is < 1 or > 64 || !Enum.IsDefined(input.Language) || !Enum.IsDefined(input.Kind)
            || !Enum.IsDefined(input.ContextKind)) return false;
        var boundaries = LearningExplanationText.Boundaries(input.Context);
        return input.Ranges.All(range => LearningExplanationText.ValidRange(range.Location, range.Length, input.Context, boundaries));
    }

    public static bool IsValid(LearningExplanationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return LearningExplanationText.Fits(result.Meaning, 2_000) && LearningExplanationText.Fits(result.Explanation, 16_000)
            && LearningExplanationText.Trim(result.Meaning).Length > 0 && LearningExplanationText.Trim(result.Explanation).Length > 0
            && InsertionSafetyGate.IsSafeToInsert(result.Meaning) && InsertionSafetyGate.IsSafeToInsert(result.Explanation);
    }

    public static LearningExplanationResult Parse(string raw)
    {
        if (!LearningExplanationText.Fits(raw, 24_000)) throw InvalidContent();
        string text = LearningExplanationText.Trim(raw);
        // Swift firstIndex(of: "\n") searches Characters. CRLF is one Character, so it does not
        // qualify as the outer fence's newline; accepting it here would diverge from the Mac rule.
        int newline = -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r')) { newline = i; break; }
        if (text.StartsWith("```", StringComparison.Ordinal) && newline >= 0 && text.EndsWith("```", StringComparison.Ordinal))
            text = text[(newline + 1)..^3];
        string? meaning = null, explanation = null;
        try
        {
            // JSONDecoder accepts one UTF-8 BOM and permits at most 512 nested containers,
            // including the root object. JsonDocument's defaults differ at both boundaries.
            using var document = JsonDocument.Parse(text.StartsWith('\uFEFF') ? text[1..] : text,
                new JsonDocumentOptions { MaxDepth = 512 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            // Mac JSONDecoder uses the first occurrence of a duplicate key. GetProperty instead
            // chooses the last in .NET, so enumerate without overwriting either first field.
            bool foundMeaning = false, foundExplanation = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!foundMeaning && property.NameEquals("meaning"))
                {
                    meaning = property.Value.GetString(); foundMeaning = true;
                }
                else if (!foundExplanation && property.NameEquals("explanation"))
                {
                    explanation = property.Value.GetString(); foundExplanation = true;
                }
            }
            if (meaning is null || explanation is null) throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new LearningExplanationException(LearningExplanationFailure.IncompleteResponse);
        }
        var result = new LearningExplanationResult(LearningExplanationText.Trim(meaning), LearningExplanationText.Trim(explanation));
        if (!IsValid(result)) throw InvalidContent();
        return result;
    }

    private static LearningExplanationException InvalidContent() => new(LearningExplanationFailure.InvalidContent);
}
