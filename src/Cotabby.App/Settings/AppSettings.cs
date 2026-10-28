using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cotabby.Core.Emoji;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Overlay;
using Cotabby.Core.Spelling;
using Cotabby.Core.Suggestions;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Core.Support;

namespace Cotabby.App.Settings;

public enum EngineKind
{
    Local,
    Endpoint,
}

/// <summary>
/// Selects the provider for the learning task only. It is deliberately independent of
/// <see cref="EngineKind"/> so changing autocomplete can never redirect a captured sentence.
/// </summary>
public enum LearningTranslationBackend
{
    Local,
    Endpoint,
}

/// <summary>
/// User preferences persisted to %APPDATA%\Cotabby\settings.json. Missing keys
/// deserialize to their defaults, so older files keep working.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Returns fresh general/runtime defaults while retaining independent notebook and
    /// review and speech preferences. Mac's reset removes an explicit key set; graph and Anki values live
    /// outside it; opt-in request history is retained app state as well. AppHost replaces its
    /// current owner, then persists/applies it once.</summary>
    public AppSettings ResetGeneralPreferences(long wordsAccepted) => new()
    {
        ActiveModelId = ActiveModelId,
        ShowFirstRunWelcome = ShowFirstRunWelcome,
        WordsAccepted = wordsAccepted,
        VocabularyGraphSettingsValue = VocabularyGraphSettingsValue,
        VocabularyBrowserModeValue = VocabularyBrowserModeValue,
        VocabularyListDensityValue = VocabularyListDensityValue,
        AnkiReviewAddressValue = AnkiReviewAddressValue, AnkiReviewDeckValue = AnkiReviewDeckValue,
        AnkiReviewModelValue = AnkiReviewModelValue, AnkiReviewMappingValue = AnkiReviewMappingValue,
        AnkiReviewRecognitionValue = AnkiReviewRecognitionValue, AnkiReviewGapValue = AnkiReviewGapValue,
        SpeechProviderValue = SpeechProviderValue, SpeechLocalModelValue = SpeechLocalModelValue,
        SpeechLocalProjectionValue = SpeechLocalProjectionValue, SpeechReferenceAudioValue = SpeechReferenceAudioValue,
        SpeechEndpointUrlValue = SpeechEndpointUrlValue, SpeechEndpointModelValue = SpeechEndpointModelValue,
        SpeechEndpointVoiceValue = SpeechEndpointVoiceValue, SpeechRateValue = SpeechRateValue,
        SpeechVoiceIdsValue = SpeechVoiceIdsValue, SpeechParametersValue = SpeechParametersValue,
        PerformanceMetricEntriesValue = PerformanceMetricEntriesValue,
    };

    // General
    public bool Enabled { get; set; } = true;
    public PauseState? Pause { get; set; }
    public bool PauseOnBatterySaver { get; set; } = true;
    public bool LaunchAtLogin { get; set; }
    public bool ShowFirstRunWelcome { get; set; } = true;
    public bool ShowTrayWordCount { get; set; } = true;
    public long WordsAccepted { get; set; }

    // Engine
    public EngineKind Engine { get; set; } = EngineKind.Local;
    public string? ActiveModelId { get; set; }
    public string EndpointBaseUrl { get; set; } = OpenAICompatibleEndpointConfiguration.DefaultBaseUrl;
    public string EndpointModel { get; set; } = "";
    public bool EndpointUsesChatApi { get; set; }
    public int DebounceMs { get; set; } = 20;

    /// <summary>Switches the local model with the power source. An empty id keeps the active model.</summary>
    public bool PowerModelSwitching { get; set; }
    public string BatteryModelId { get; set; } = "";
    public string PluggedInModelId { get; set; } = "";

    // Writing
    public string UserName { get; set; } = "";
    public List<string> Languages { get; set; } = [.. LanguageCatalog.DefaultLanguages];
    public string CompletionLengthPreset { get; set; } = WordCountPreset.Default.Id;
    public bool UseCustomWordRange { get; set; }
    public int CustomLowWords { get; set; } = 5;
    public int CustomHighWords { get; set; } = 15;
    public bool AllowMultiLine { get; set; }
    public bool SuggestWithinWords { get; set; } = true;
    public bool AcceptPunctuationWithWord { get; set; } = true;
    public bool AddSpaceAfterAccept { get; set; }
    public AcceptanceGranularity AcceptanceMode { get; set; }

    // Corrections
    public bool SuppressCompletionsOnTypo { get; set; } = true;
    public bool OfferTypoCorrections { get; set; } = true;
    public bool AutoFixTypos { get; set; }
    public List<string> SpellingDictionaries { get; set; } = [.. SpellingDictionaryCatalog.DefaultEnabledCodes];

    // Context
    public bool ClipboardContext { get; set; }

    /// <summary>Tells the model which app and window the text is being written in.</summary>
    public bool IncludeAppContext { get; set; } = true;
    public string ExtendedContext { get; set; } = "";

    // Shortcuts
    public KeyBinding AcceptWord { get; set; } = KeyBinding.Tab;
    public KeyBinding AcceptAll { get; set; } = KeyBinding.Backtick;
    public KeyBinding GlobalToggle { get; set; } = KeyBinding.Unbound;
    /// <summary>Explicit, manual current-sentence translation. The Mac Option+T binding maps to Alt+T on Windows.</summary>
    public KeyBinding LearningTranslationShortcut { get; set; } = new(0x54, KeyModifiers.Alt, "Alt+T");
    public bool DoubleTapAcceptsAll { get; set; }
    public List<PerAppShortcutOverride> ShortcutOverrides { get; set; } = [];

    // Language learning keeps its backend and both provider profiles task-local. Existing endpoint
    // keys remain stable; the new backend key defaults to local, and Parse migrates only old users
    // who had explicitly enabled the learning endpoint.
    public bool LearningTranslationEnabled { get; set; }
    public LearningTranslationBackend LearningBackend { get; set; } = LearningTranslationBackend.Local;
    public bool LearningEndpointEnabled { get; set; }
    public string LearningEndpointBaseUrl { get; set; } = OpenAICompatibleEndpointConfiguration.DefaultBaseUrl;
    public string LearningEndpointModel { get; set; } = "";
    /// <summary>The macOS learning profile's default context size; serialized values remain unchanged.</summary>
    public int LearningContextTokens { get; set; } = 8_192;
    /// <summary>The installed GGUF filename selected only for local learning inference.</summary>
    public string LearningLocalModel { get; set; } = "";
    /// <summary>The requested instruction format for local learning models; Automatic reads GGUF metadata.</summary>
    [JsonConverter(typeof(LearningChatTemplateJsonConverter))]
    public LearningChatTemplate LearningLocalTemplate { get; set; } = LearningChatTemplate.Automatic;
    /// <summary>Per-model local translation context preferences; endpoint context remains in LearningContextTokens.</summary>
    public Dictionary<string, int> LearningLocalContextTokensByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-model local style/sampling preferences, independent of the endpoint generation profile.</summary>
    public Dictionary<string, LearningGenerationProfile> LearningLocalGenerationProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sanitizes unknown persisted values to the safe local default.</summary>
    [JsonIgnore]
    public LearningTranslationBackend EffectiveLearningBackend =>
        Enum.IsDefined(LearningBackend) ? LearningBackend : LearningTranslationBackend.Local;

    /// <summary>
    /// Returns one detached and bounded local generation profile. Local profiles are keyed by the
    /// installed filename, matching the Mac task model identity while keeping endpoint preferences
    /// and autocomplete model settings out of the translation request.
    /// </summary>
    public LearningGenerationProfile EffectiveLearningLocalGenerationProfile(string modelFileName)
    {
        var profile = !string.IsNullOrEmpty(modelFileName)
            && LearningLocalGenerationProfiles is not null
            && LearningLocalGenerationProfiles.TryGetValue(modelFileName, out var saved)
            ? saved
            : null;
        return new LearningGenerationProfile
        {
            // Keep the bounded custom instruction while another register is selected. The
            // request snapshot applies Effective later, but durable settings should not discard a
            // user's dormant custom value when switching Natural/Formal/Conversational.
            Style = (profile?.Style ?? new LearningTranslationStyle()).Normalized,
            Sampling = (profile?.Sampling ?? new LearningModelSamplingOverrides()).Normalized,
            MaximumOutputTokens = profile?.MaximumOutputTokens is { } maximum
                ? Math.Clamp(maximum, 256, ModelContextBudget.MaximumTranslationOutputTokens)
                : null,
        };
    }

    /// <summary>Uses the Mac catalog recommendation when present and otherwise its 8,192-token fallback.</summary>
    public int EffectiveLearningLocalContextTokens(string modelFileName)
    {
        if (!string.IsNullOrEmpty(modelFileName)
            && LearningLocalContextTokensByModel is not null
            && LearningLocalContextTokensByModel.TryGetValue(modelFileName, out int saved))
        {
            return Math.Clamp(saved, 1_024, ModelContextBudget.MaximumLocalTokens);
        }

        return LearningRuntimeCatalog.DefaultTranslationContextWindowTokens(modelFileName)
            ?? 8_192;
    }
    /// <summary>
    /// Enabled source-language choices. Null means this settings file predates the multi-source
    /// preference and should be read through <see cref="LearningSourceLanguage"/>; an explicit
    /// empty list remains empty so the UI never silently restores a language the user removed.
    /// </summary>
    public List<LearningLanguage>? LearningSourceLanguages { get; set; }

    /// <summary>
    /// A detached, ordered snapshot of usable source choices. The singular preference stays
    /// serialized for older builds; once the list exists, it is the complete source policy.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<LearningLanguage> EffectiveLearningSourceLanguages
    {
        get
        {
            var configured = LearningSourceLanguages;
            if (configured is null)
            {
                configured = [Enum.IsDefined(LearningSourceLanguage)
                    ? LearningSourceLanguage
                    : LearningLanguage.Chinese];
            }

            var snapshot = configured
                .Where(language => Enum.IsDefined(language))
                .Distinct()
                .ToArray();
            return Array.AsReadOnly(snapshot);
        }
    }

    /// <summary>Legacy one-source key retained for migration from pre-list settings files.</summary>
    public LearningLanguage LearningSourceLanguage { get; set; } = LearningLanguage.Chinese;
    public LearningLanguage LearningTargetLanguage { get; set; } = LearningLanguage.English;
    public LearningTranslationRegister LearningStyleRegister { get; set; } = LearningTranslationRegister.Natural;
    public string LearningCustomInstruction { get; set; } = "";

    // Apps
    public List<string> BlockedApps { get; set; } = [];
    public bool SuggestInIntegratedTerminals { get; set; }

    // Appearance
    /// <summary>"auto" follows the system theme. Anything else is a "#RRGGBB" color.</summary>
    public string GhostTextColor { get; set; } = "auto";
    public double GhostTextOpacity { get; set; } = 1.0;
    public double GhostTextSizeMultiplier { get; set; } = 1.0;
    public bool ShowAcceptanceHint { get; set; } = true;
    public SuggestionDisplayMode DisplayMode { get; set; }
    public bool FadeInSuggestions { get; set; } = true;
    public double FadeInSeconds { get; set; } = 0.10;

    // Inline features
    public bool EmojiPickerEnabled { get; set; } = true;
    public bool MacrosEnabled { get; set; } = true;
    public EmojiSkinTone EmojiSkinTone { get; set; }
    public EmojiGender EmojiGender { get; set; }

    /// <summary>The effective word range, from the preset or the custom bounds.</summary>
    [JsonIgnore]
    public (int Low, int High) WordRange =>
        UseCustomWordRange
            ? WordCountPreset.Clamp(CustomLowWords, CustomHighWords)
            : WordCountPreset.Find(CompletionLengthPreset).Range;

    /// <summary>The snapshot the suggestion pipeline reads.</summary>
    public SuggestionSettings ToSuggestionSettings() => new()
    {
        Enabled = Enabled,
        Pause = Pause,
        PauseOnBatterySaver = PauseOnBatterySaver,
        BlockedApps = new HashSet<string>(BlockedApps, StringComparer.OrdinalIgnoreCase),
        SuggestInIntegratedTerminals = SuggestInIntegratedTerminals,
        DebounceMs = Math.Clamp(DebounceMs, 0, 500),
        UsesEndpoint = Engine == EngineKind.Endpoint,
        SurfaceContext = IncludeAppContext,
        WordRange = new SuggestionWordRange(WordRange.Low, WordRange.High),
        AllowMultiLine = AllowMultiLine,
        AcceptWord = AcceptWord,
        AcceptAll = DoubleTapAcceptsAll ? KeyBinding.Unbound : AcceptAll,
        ShortcutOverrides = ShortcutOverrides.ToList(),
        DoubleTapAcceptsAll = DoubleTapAcceptsAll && AcceptWord.IsBound,
        Granularity = AcceptanceMode,
        AcceptPunctuationWithWord = AcceptPunctuationWithWord,
        AddSpaceAfterAccept = AddSpaceAfterAccept,
        DisplayMode = DisplayMode,
        SuggestWithinWords = SuggestWithinWords,
        // Offering a fix needs typo detection on, and fixing automatically needs offering on.
        Typos = new TypoGateSettings(
            SuppressCompletionsOnTypo,
            SuppressCompletionsOnTypo && OfferTypoCorrections,
            SuppressCompletionsOnTypo && OfferTypoCorrections && AutoFixTypos),
        SpellingDictionaries = SpellingDictionaryCatalog.Normalize(SpellingDictionaries),
        UserName = UserName,
        ExtendedContext = ExtendedContext,
        ClipboardContext = ClipboardContext,
        Languages = LanguageCatalog.Normalize(Languages),
    };
}

/// <summary>The curated completion lengths. Mirrors the macOS word count presets.</summary>
public sealed record WordCountPreset(string Id, string Title, (int Low, int High) Range)
{
    public const int MinimumWords = 1;
    public const int MaximumWords = 50;

    public static IReadOnlyList<WordCountPreset> All { get; } =
    [
        new("very_short", "Very Short", (2, 4)),
        new("short", "Short", (4, 7)),
        new("medium", "Medium", (7, 12)),
        new("long", "Long", (12, 20)),
    ];

    public static WordCountPreset Default => All[3];

    public string Label => $"{Range.Low}-{Range.High} words";

    public static WordCountPreset Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;

    /// <summary>Clamps both ends into range and keeps low at or below high.</summary>
    public static (int Low, int High) Clamp(int low, int high)
    {
        low = Math.Clamp(low, MinimumWords, MaximumWords);
        return (low, Math.Clamp(high, low, MaximumWords));
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/>. The file is a few KB, so IO is synchronous.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep previously persisted enum names stable, while the newly introduced learning model
        // template and seed mode retain their dedicated Mac-shaped durable identifiers.
        Converters =
        {
            new LearningChatTemplateJsonConverter(),
            new LearningModelSeedModeJsonConverter(),
            new JsonStringEnumConverter(),
        },
    };

    public string Path { get; }

    public SettingsStore()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cotabby");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        try
        {
            return Parse(File.ReadAllText(Path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, Serialize(settings));
        File.Move(tmp, Path, overwrite: true);
    }

    // Keep the exact persisted representation testable without creating the user settings folder.
    internal static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOpts);

    public static AppSettings Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return new AppSettings();
        MigrateLegacyKeys(root);
        var settings = root.Deserialize<AppSettings>(JsonOpts) ?? new AppSettings();
        if (!root.ContainsKey(nameof(AppSettings.LearningBackend)))
        {
            // Prior Windows builds had only the learning endpoint consent flag. Preserve those
            // explicitly opted-in users, while a fresh install or an unconfigured old profile
            // starts local. A Local selection never consumes the dormant endpoint consent.
            settings.LearningBackend = settings.LearningEndpointEnabled
                ? LearningTranslationBackend.Endpoint
                : LearningTranslationBackend.Local;
        }

        settings.LearningBackend = settings.EffectiveLearningBackend;
        settings.LearningLocalModel ??= string.Empty;
        // JSON creates new dictionaries with its default comparer. Windows filenames remain
        // case-insensitive after restart too; replaying entries also handles case-only duplicate
        // keys deterministically without rejecting the rest of the user's preferences.
        settings.LearningLocalContextTokensByModel = CaseInsensitiveModelMap(settings.LearningLocalContextTokensByModel);
        settings.LearningLocalGenerationProfiles = CaseInsensitiveModelMap(settings.LearningLocalGenerationProfiles);
        settings.LearningExplanationLocalModel ??= string.Empty;
        settings.LearningExplanationLocalContextTokensByModel = CaseInsensitiveModelMap(settings.LearningExplanationLocalContextTokensByModel);
        settings.LearningExplanationLocalGenerationProfiles = CaseInsensitiveModelMap(settings.LearningExplanationLocalGenerationProfiles);
        if (!Enum.IsDefined(settings.LearningExplanationLocalTemplate))
            settings.LearningExplanationLocalTemplate = LearningChatTemplate.Automatic;
        if (!Enum.IsDefined(settings.LearningLocalTemplate))
            settings.LearningLocalTemplate = LearningChatTemplate.Automatic;

        if (!root.ContainsKey(nameof(AppSettings.LearningSourceLanguages)))
        {
            // Older files stored one explicit source. Materialize it as a one-item choice list so
            // the first settings save cannot lose the old direction while the UI moves to the
            // Mac-shaped multi-source preference. EffectiveLearningSourceLanguages validates it.
            settings.LearningSourceLanguages = settings.EffectiveLearningSourceLanguages.ToList();
        }

        return settings;
    }

    private static Dictionary<string, T> CaseInsensitiveModelMap<T>(Dictionary<string, T>? saved)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (saved is not null)
            foreach (var pair in saved) result[pair.Key] = pair.Value;
        return result;
    }

    /// <summary>Files written before shortcuts were recordable stored the accept-all key by name.</summary>
    private static void MigrateLegacyKeys(JsonObject root)
    {
        if (root["AcceptSuggestionKey"] is JsonValue legacy && !root.ContainsKey(nameof(AppSettings.AcceptAll)))
        {
            var binding = legacy.GetValue<string>().Trim().ToLowerInvariant() switch
            {
                "tilde" or "~" => new KeyBinding(KeyBinding.VkBacktick, KeyModifiers.Shift, "~"),
                "backslash" or "\\" => new KeyBinding(0xDC, KeyModifiers.None, "\\"),
                "" => KeyBinding.Unbound,
                _ => KeyBinding.Backtick,
            };
            root[nameof(AppSettings.AcceptAll)] = JsonSerializer.SerializeToNode(binding, JsonOpts);
        }
    }
}
