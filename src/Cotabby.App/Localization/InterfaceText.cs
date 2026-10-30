using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Cotabby.Core.Settings;
using Cotabby.Win32.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.RegularExpressions;

namespace Cotabby.App.Localization;

/// <summary>Launch-only interface locale. Catalog lookups accept explicitly authored interface
/// keys, never user text. Missing translations fall back to the English source key.</summary>
internal static class InterfaceText
{
    private static IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _tables = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> _tableKeys = new Dictionary<string, IReadOnlySet<string>>();
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _quantityTables = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>();
    private static IReadOnlyDictionary<(string Table, string Key), IReadOnlyDictionary<string, string>> _englishQuantities = new Dictionary<(string, string), IReadOnlyDictionary<string, string>>();
    internal static string? LaunchSelection { get; private set; }
    internal static Func<Task>? RequestRestart { get; set; }
    internal static string Locale { get; private set; } = "en";
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Locale);
    internal static void Initialize(string? stored, string? systemLocale = null)
    {
        LaunchSelection = stored is null ? null : InterfaceLanguagePolicy.Normalize(stored) ?? stored;
        var explicitLocale = stored is null ? null : InterfaceLanguagePolicy.Normalize(stored);
        if (explicitLocale is not null) Locale = explicitLocale;
        else
        {
            var preferred = systemLocale is null ? WindowsPreferredUiLanguages.Read() : [systemLocale];
            Locale = InterfaceLanguagePolicy.FirstSupported(preferred.Count == 0 ? [CultureInfo.CurrentUICulture.Name] : preferred);
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var tableKeys = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var quantities = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
        var englishQuantities = new Dictionary<(string, string), IReadOnlyDictionary<string, string>>();
        foreach (var name in new[] { "Localizable", "Learning", "Vocabulary", "Windows" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cotabby.InterfaceLanguage.{name}.xcstrings");
            if (stream is null) continue;
            using var json = JsonDocument.Parse(stream);
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            var quantityTable = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            tableKeys[name] = json.RootElement.GetProperty("strings").EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var item in json.RootElement.GetProperty("strings").EnumerateObject())
            {
                if (!item.Value.TryGetProperty("localizations", out var locales)) continue;
                if (locales.TryGetProperty("en", out var english) && english.TryGetProperty("variations", out var englishVariations)
                    && englishVariations.TryGetProperty("plural", out var englishPlural)) englishQuantities[(name, item.Name)] = ReadPluralForms(englishPlural);
                if (!locales.TryGetProperty(Locale, out var translation))
                {
                    // Keep ordinary labels' established source-key fallback. Only quantity
                    // resources need their English forms when this locale has none.
                    if (!locales.TryGetProperty("en", out translation) || !translation.TryGetProperty("variations", out _)) continue;
                }
                if (translation.TryGetProperty("variations", out var variations) && variations.TryGetProperty("plural", out var plural))
                {
                    quantityTable[item.Name] = ReadPluralForms(plural);
                }
                if (!translation.TryGetProperty("stringUnit", out var unit) || !unit.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.String) continue;
                string translated = value.GetString()!;
                // Some copied upstream translations still name the old product although the
                // authored key no longer does. Adapt trusted UI resources only; explicit old
                // project attribution and all later format arguments retain their exact text.
                if (!item.Name.Contains("Cotabby", StringComparison.Ordinal))
                    translated = translated.Replace("Cotabby", "Lingrove", StringComparison.Ordinal);
                table[item.Name] = translated;
                result.TryAdd(item.Name, translated);
            }
            tables[name] = table;
            quantities[name] = quantityTable;
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
        _strings = result; _tables = tables; _tableKeys = tableKeys; _quantityTables = quantities; _englishQuantities = englishQuantities;
    }
    private static IReadOnlyDictionary<string, string> ReadPluralForms(JsonElement plural)
    {
        var forms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var form in plural.EnumerateObject())
            if (form.Value.TryGetProperty("stringUnit", out var unit) && unit.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String) forms[form.Name] = value.GetString()!;
        return forms;
    }
    internal static string Get(string source) => Get(source, null);
    internal static string Get(string source, string? table)
    {
        if (table is null || source == "Follow System") return TryTranslation(_strings, source, out var value) ? value : source;
        if (_tables.TryGetValue(table, out var catalog) && TryTranslation(catalog, source, out var translated)) return translated;
        if (_tables.TryGetValue("Windows", out var platform) && TryTranslation(platform, source, out var adapted)) return adapted;
        // A known table keeps its own missing-translation fallback instead of borrowing a
        // conflicting interpretation of the same English key from another Mac table.
        if (_tableKeys.TryGetValue(table, out var keys) && (keys.Contains(source) || keys.Contains(source.Replace("Lingrove", "Cotabby", StringComparison.Ordinal)))) return source;
        return TryTranslation(_strings, source, out var shared) ? shared : source;
    }
    private static bool TryTranslation(IReadOnlyDictionary<string, string> catalog, string source, out string value)
    {
        if (catalog.TryGetValue(source, out value!)) return true;
        // Reuse translations of the same authored UI sentence under its original product key.
        // Explicit historical attribution does not name Lingrove and never takes this branch.
        if (source.Contains("Lingrove", StringComparison.Ordinal)
            && catalog.TryGetValue(source.Replace("Lingrove", "Cotabby", StringComparison.Ordinal), out var legacy))
        { value = legacy.Replace("Cotabby", "Lingrove", StringComparison.Ordinal); return true; }
        value = source; return false;
    }

    /// <summary>Translate the authored template before inserting values. Arguments, filenames,
    /// model names and user text never become catalog keys. Mac printf placeholders stay in
    /// the copied catalogs and are converted only for this .NET presentation adapter.</summary>
    internal static string Format(string source, params object?[] arguments)
    {
        string key = source == "Version {0}" ? "Version %@" : source;
        return FormatTemplate(Get(key), arguments);
    }
    /// <summary>Read the original xcstrings plural forms, then select with the installed Windows
    /// ICU grammar. This is interface copy only: it does not reinterpret any generated/task text.</summary>
    internal static string Quantity(string source, int count, string table = "Localizable") =>
        QuantityWithCategory(source, count, table, WindowsPluralRules.Cardinal(Locale, count));
    internal static string QuantityWithCategory(string source, int count, string table, string? category)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        // Missing native grammar uses the catalog's known English source forms rather than
        // guessing a one/few/many category for the selected interface language.
        if (category is null && _englishQuantities.TryGetValue((table, source), out var english))
        {
            if (english.TryGetValue(count == 1 ? "one" : "other", out var fallback)) return FormatTemplate(fallback, [count]);
        }
        if (_quantityTables.TryGetValue(table, out var catalog) && catalog.TryGetValue(source, out var forms))
        {
            if (category is not null && forms.TryGetValue(category, out var value)) return FormatTemplate(value, [count]);
            if (forms.TryGetValue("other", out var other)) return FormatTemplate(other, [count]);
        }
        return FormatTemplate(Get(source, table), [count]);
    }
    private static string FormatTemplate(string template, object?[] arguments)
    {
        int next = 0;
        template = Regex.Replace(template, @"%(?:(\d+)\$)?(?:ll|l)?([@diu]|\.\d+f|f|%)", match =>
        {
            string type = match.Groups[2].Value;
            if (type == "%") return "%";
            int index = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1 : next++;
            string format = type.EndsWith('f') ? ":F" + (type.StartsWith('.') ? type[1..^1] : "6") : "";
            return "{" + index.ToString(CultureInfo.InvariantCulture) + format + "}";
        });
        return string.Format(Culture, template, arguments);
    }
    internal static void ApplyDirection(Window window) => window.FlowDirection = Locale == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Only call immediately after InitializeComponent, before inserting user content or
    /// binding live state. Bound text and editable inputs are deliberately excluded.</summary>
    internal static void TranslateInitialTree(DependencyObject root) => TranslateInitialTree(root, null);
    internal static void TranslateInitialTree(DependencyObject root, string? table)
    {
        if (root is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty)) text.Text = Get(text.Text, table);
        if (root is ContentControl control && control.Content is string caption
            && !BindingOperations.IsDataBound(control, ContentControl.ContentProperty)) control.Content = Get(caption, table);
        if (root is HeaderedContentControl) TranslateLocal(root, HeaderedContentControl.HeaderProperty, table);
        if (root is HeaderedItemsControl) TranslateLocal(root, HeaderedItemsControl.HeaderProperty, table);
        if (root is Window) TranslateLocal(root, Window.TitleProperty, table);
        if (root is FrameworkElement) TranslateLocal(root, FrameworkElement.ToolTipProperty, table);
        TranslateLocal(root, System.Windows.Automation.AutomationProperties.NameProperty, table);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) TranslateInitialTree(child, table);
    }
    private static void TranslateLocal(DependencyObject target, DependencyProperty property, string? table)
    {
        // A local string is an authored XAML label here. Expressions/resources and bindings
        // retain their owners; do not replace a live binding with its currently displayed value.
        if (!BindingOperations.IsDataBound(target, property) && target.ReadLocalValue(property) is string source)
            target.SetValue(property, Get(source, table));
    }
}
