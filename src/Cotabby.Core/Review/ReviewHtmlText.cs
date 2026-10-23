namespace Cotabby.Core.Review;

/// <summary>
/// The literal escaping primitive from Mac ReviewCardPolicy.html. Content/review serializers borrow
/// this stateless rule; user text never becomes markup. Preserve CR and other original characters,
/// replacing only LF after escaping, as the reference does. This is not an HTML document sanitizer.
/// </summary>
public static class ReviewHtmlText
{
    public static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&#39;", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);
}
