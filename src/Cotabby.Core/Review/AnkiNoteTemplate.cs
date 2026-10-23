using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>Original static recognition/gap templates and silent preview renderer. Only these
/// known templates are interpreted; prepared field HTML is inserted once after conditionals, so
/// braces in user content remain literal. No WebView, audio playback or Anki write is performed.</summary>
public static partial class AnkiNoteTemplate
{
    public const string LegacyCss = """
    .card { font-family: -apple-system, sans-serif; font-size: 22px; line-height: 1.6;
      text-align: left; color: #202124; background: #fff; padding: 24px; }
    b { color: #087cff; } .meaning { font-weight: 600; } .extra { font-size: 17px; opacity: .75; }
    .nightMode.card { color: #eee; background: #202124; } hr { opacity: .2; }
    """;
    public const string LegacyRecognitionFront = "{{#Recognition}}{{#Sentence}}{{Sentence}}{{/Sentence}}{{^Sentence}}{{Expression}}{{/Sentence}}{{/Recognition}}";
    public const string LegacyGapFront = "{{#GapPrompt}}{{GapPrompt}}<div class='extra'>{{Meaning}}</div>{{/GapPrompt}}";
    public const string LegacyRecognitionBack = """
    {{FrontSide}}<hr id=answer><div class='meaning'>{{Meaning}}</div><div>{{Expression}}</div>
    <div class='extra'>{{Translation}}</div><div class='extra'>{{Notes}}</div><div class='extra'>{{Explanation}}</div>
    """;
    public const string LegacyGapBack = """
    {{FrontSide}}<hr id=answer><div>{{GapAnswer}}</div><div>{{Sentence}}</div>
    <div class='meaning'>{{Meaning}}</div><div>{{Expression}}</div>
    <div class='extra'>{{Translation}}</div><div class='extra'>{{Notes}}</div><div class='extra'>{{Explanation}}</div>
    """;
    public const string Css = """
    .card { font-family: -apple-system, sans-serif; font-size: 22px; line-height: 1.65;
      text-align: left; color: #202124; background: #fff; padding: 20px; overflow-wrap: anywhere; }
    .prompt { margin-bottom: 16px; } b { color: #087cff; font-weight: 650; }
    .meaning { font-size: 1.1em; font-weight: 600; margin: 12px 0; }
    .expression { font-weight: 600; margin-top: 12px; } .extra { font-size: .8em; opacity: .78; margin: 10px 0; }
    .audio { margin: 8px 0; } hr { border: 0; border-top: 1px solid #8885; margin: 20px 0; }
    .nightMode.card { color: #eee; background: #202124; }
    @media (prefers-color-scheme: dark) { .card { color: #eee; background: #202124; } }
    """;
    public const string RecognitionFront = """{{#Recognition}}<div class="prompt">{{#Sentence}}{{Sentence}}{{/Sentence}}{{^Sentence}}{{Expression}}{{/Sentence}}</div>{{/Recognition}}""";
    public const string GapFront = "{{#GapPrompt}}<div class='prompt'>{{GapPrompt}}</div>{{/GapPrompt}}";
    public const string RecognitionBack = """
    {{FrontSide}}<hr id=answer><div class='meaning'>{{Meaning}}</div>
    <div class='expression'>{{Expression}}</div><div class='audio'>{{ExpressionAudio}}</div>
    <div class='audio'>{{SentenceAudio}}</div><div class='extra'>{{Translation}}</div>
    <div class='extra'>{{Notes}}</div><div class='extra'>{{Explanation}}</div>
    """;
    public const string GapBack = """
    {{FrontSide}}<hr id=answer><div class='prompt'>{{Sentence}}</div>
    <div class='meaning'>{{Meaning}}</div><div class='expression'>{{Expression}}</div>
    <div class='audio'>{{ExpressionAudio}}</div><div class='audio'>{{SentenceAudio}}</div>
    <div class='extra'>{{Translation}}</div><div class='extra'>{{Notes}}</div><div class='extra'>{{Explanation}}</div>
    """;
    public static IReadOnlyDictionary<string, AnkiValue> Templates => TemplateValues(false);
    public static IReadOnlyDictionary<string, AnkiValue> LegacyTemplates => TemplateValues(true);
    private static IReadOnlyDictionary<string, AnkiValue> TemplateValues(bool legacy) => CollectionValues.Map(new Dictionary<string, AnkiValue>
    {
        ["Recognition"] = AnkiValue.Strings(new Dictionary<string, string> { ["Front"] = legacy ? LegacyRecognitionFront : RecognitionFront, ["Back"] = legacy ? LegacyRecognitionBack : RecognitionBack }),
        ["Gap"] = AnkiValue.Strings(new Dictionary<string, string> { ["Front"] = legacy ? LegacyGapFront : GapFront, ["Back"] = legacy ? LegacyGapBack : GapBack }),
    });
    public static string Preview(IReadOnlyDictionary<string, string> fields, bool gap, bool back, string css = Css, bool legacy = false)
    {
        string frontTemplate = gap ? legacy ? LegacyGapFront : GapFront : legacy ? LegacyRecognitionFront : RecognitionFront;
        string backTemplate = gap ? legacy ? LegacyGapBack : GapBack : legacy ? LegacyRecognitionBack : RecognitionBack;
        string front = Render(frontTemplate, fields); var sides = new Dictionary<string, string>(fields) { ["FrontSide"] = front };
        string body = back ? Render(backTemplate, sides) : front;
        return "<style>" + css + "</style><div class='card'>" + Sound().Replace(body, "") + "</div>";
    }
    private static string Render(string template, IReadOnlyDictionary<string, string> fields)
    {
        string text = template;
        while (Conditional().Match(text) is { Success: true } match)
        {
            bool present = (fields.GetValueOrDefault(match.Groups[2].Value) ?? "").Length != 0, inverse = match.Groups[1].Value == "^";
            text = text.Remove(match.Index, match.Length).Insert(match.Index, present != inverse ? match.Groups[3].Value : "");
        }
        var result = new StringBuilder(text);
        foreach (Match match in Variable().Matches(text).Reverse()) result.Remove(match.Index, match.Length).Insert(match.Index, fields.GetValueOrDefault(match.Groups[1].Value) ?? "");
        return result.ToString();
    }
    [GeneratedRegex(@"\{\{([#^])(\w+)\}\}((?:(?!\{\{[#^]).)*?)\{\{/\2\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Conditional();
    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.CultureInvariant)] private static partial Regex Variable();
    [GeneratedRegex(@"\[sound:[^\]]+\]", RegexOptions.CultureInvariant)] private static partial Regex Sound();
}
