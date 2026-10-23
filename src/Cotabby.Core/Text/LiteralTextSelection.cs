namespace Cotabby.Core.Text;

/// <summary>
/// A readonly view's exact UTF-16 selection, independent of an editor or insertion authority.
/// WPF hit testing supplies two offsets; grapheme expansion keeps a combining sequence, emoji or
/// CRLF together. The original string and its spelling/whitespace are never normalized or trimmed.
/// A control owns this short-lived value and replaces it when its document or literal text changes.
/// </summary>
public sealed record LiteralTextSelection
{
    internal LiteralTextSelection(string source, int start, int length) { Source = source; Start = start; Length = length; }
    public string Source { get; }
    public int Start { get; }
    public int Length { get; }
    public string Text => Source.Substring(Start, Length);

    public static LiteralTextSelection? Create(string text, int anchor, int extent)
    {
        return LiteralTextSelectionMap.Create(text)?.Select(anchor, extent);
    }
}
