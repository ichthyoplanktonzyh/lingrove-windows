using System.Text.RegularExpressions;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Mac 3b article envelope. The renderer must additionally disable scripts and
/// intercept every request; this string is not a security boundary by itself.</summary>
public static class DictionaryHtmlPolicy
{
    public static string Document(string html)
    {
        string content = Regex.Replace(html, @"<base\b[^>]*>|<meta\b[^>]*>|<!doctype[^>]*>|</?(?:html|head|body)\b[^>]*>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return """
            <!doctype html><html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none';
            script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; form-action 'none';
            base-uri 'none'; img-src lingrove-dictionary:; style-src 'unsafe-inline' lingrove-dictionary:;
            font-src lingrove-dictionary:; media-src 'none'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>:root{color-scheme:light dark}html,body{margin:0;
            padding:0;background:transparent;color:CanvasText;font:13px 'Segoe UI';overflow-wrap:anywhere}
            img{max-width:100%;height:auto}pre{white-space:pre-wrap}a{color:LinkText}table{max-width:100%}</style>
            </head><body>
            """ + content + "</body></html>";
    }
}
