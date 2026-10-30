namespace Cotabby.Core.Settings;

/// <summary>The committed Mac CompletionPreviewText rule (3b53402). Translators move one ¦
/// inside a complete sentence; views own animation and styling. Malformed markers degrade to
/// readable typed text rather than inventing a second run or exposing the marker.</summary>
public sealed record CompletionPreviewText
{
    public string TypedText { get; }
    public string GhostText { get; }
    public CompletionPreviewText(string resolvedSentence)
    {
        var parts = resolvedSentence.Split('¦');
        TypedText = parts.Length == 2 ? parts[0] : resolvedSentence.Replace("¦", "", StringComparison.Ordinal);
        GhostText = parts.Length == 2 ? parts[1] : "";
    }
}
