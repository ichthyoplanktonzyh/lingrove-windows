using System.Globalization;
using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Preserves the Mac rule's grapheme whitespace/newline and exact hit-test behavior. Unicode
/// casing/alphabetic properties and word breaking stay at the platform boundary, while these
/// deterministic operations never normalize or replace the document being recognized.
/// </summary>
public static class LearningUnitPolicy
{
    public static LearningUnit? UnitAt(int location, IReadOnlyList<LearningUnit> units) =>
        units.FirstOrDefault(unit => unit.Ranges.Any(range => location >= range.Location
            && (long)location < (long)range.Location + range.Length));

    public static string CollapseWhitespace(string value)
    {
        var result = new StringBuilder();
        bool separated = false;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (char.IsWhiteSpace(element, 0)) { separated = result.Length > 0; continue; }
            if (separated) result.Append(' ');
            result.Append(element);
            separated = false;
        }
        return result.ToString();
    }

    internal static bool ContainsWhitespace(string value) => Elements(value).Any(element => char.IsWhiteSpace(element, 0));
    internal static bool AllWhitespace(string value) => Elements(value).All(element => char.IsWhiteSpace(element, 0));
    internal static bool ContainsNewline(string value) => Elements(value).Any(element =>
        element[0] is '\n' or '\r' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029');

    private static IEnumerable<string> Elements(string value)
    {
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext()) yield return elements.GetTextElement();
    }
}
