using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Cotabby.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Cotabby.App.Localization;

/// <summary>Launch-only interface locale. Catalog lookups accept explicitly authored interface
/// keys, never user text. Missing translations fall back to the English source key.</summary>
internal static class InterfaceText
{
    private static IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    internal static string? LaunchSelection { get; private set; }
    internal static Func<Task>? RequestRestart { get; set; }
    internal static string Locale { get; private set; } = "en";
    internal static void Initialize(string? stored, string? systemLocale = null)
    {
        LaunchSelection = stored is null ? null : InterfaceLanguagePolicy.Normalize(stored) ?? stored;
        Locale = (stored is null ? null : InterfaceLanguagePolicy.Normalize(stored))
            ?? InterfaceLanguagePolicy.Normalize(systemLocale ?? CultureInfo.CurrentUICulture.Name) ?? "en";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "Localizable", "Learning", "Vocabulary" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cotabby.InterfaceLanguage.{name}.xcstrings");
            if (stream is null) continue;
            using var json = JsonDocument.Parse(stream);
            foreach (var item in json.RootElement.GetProperty("strings").EnumerateObject())
            {
                if (!item.Value.TryGetProperty("localizations", out var locales) || !locales.TryGetProperty(Locale, out var translation)
                    || !translation.TryGetProperty("stringUnit", out var unit) || !unit.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.String) continue;
                result.TryAdd(item.Name, value.GetString()!);
            }
        }
        // The Mac strings for this choice sometimes name macOS; use platform-neutral wording.
        var followSystem = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zh-Hans"] = "跟随系统", ["zh-Hant"] = "跟隨系統", ["ja"] = "システムに従う", ["ko"] = "시스템 설정 따르기",
            ["fr"] = "Suivre le système", ["de"] = "Systemsprache verwenden", ["es"] = "Usar el idioma del sistema",
            ["pt-BR"] = "Usar idioma do sistema", ["it"] = "Usa lingua di sistema", ["ru"] = "Язык системы",
            ["ar"] = "استخدام لغة النظام", ["hi"] = "सिस्टम भाषा का उपयोग करें", ["id"] = "Ikuti bahasa sistem", ["tr"] = "Sistem dilini kullan"
        };
        if (followSystem.TryGetValue(Locale, out var follow)) result["Follow System"] = follow;
        _strings = result;
    }
    internal static string Get(string source) => _strings.TryGetValue(source, out var value) ? value : source;
    internal static void ApplyDirection(Window window) => window.FlowDirection = Locale == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Only call immediately after InitializeComponent, before inserting user content or
    /// binding live state. Bound text and editable inputs are deliberately excluded.</summary>
    internal static void TranslateInitialTree(DependencyObject root)
    {
        if (root is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty)) text.Text = Get(text.Text);
        if (root is ContentControl control && control.Content is string caption
            && !BindingOperations.IsDataBound(control, ContentControl.ContentProperty)) control.Content = Get(caption);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) TranslateInitialTree(child);
    }
}
