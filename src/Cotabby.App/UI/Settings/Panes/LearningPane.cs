using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>
/// Owns the app-facing preferences for the manual sentence preview. Settings remain separate
/// from autocomplete because the two tasks have independent providers, endpoint consent and
/// generation profiles; changing these rows is projected to the learning controller by AppHost.
/// </summary>
internal static class LearningPane
{
    private static readonly (LearningLanguage Value, string Label)[] Languages =
        [.. LearningLanguages.All.Select(language => (language, language.Label()))];

    private static readonly (LearningTranslationRegister Value, string Label)[] Registers =
    [
        (LearningTranslationRegister.Natural, "Natural"),
        (LearningTranslationRegister.Formal, "Formal"),
        (LearningTranslationRegister.Conversational, "Conversational"),
        (LearningTranslationRegister.Custom, "Custom instruction"),
    ];

    private static readonly (LearningTranslationBackend Value, string Label)[] Backends =
    [
        (LearningTranslationBackend.Local, "Local GGUF model"),
        (LearningTranslationBackend.Endpoint, "OpenAI-compatible endpoint"),
    ];

    public static SettingsPane Build(Rows rows)
    {
        var context = rows.Context;
        var review = new AnkiReviewSettingsSection(context.Host.Review);
        context.Closed += review.Close;
        var destination = Ui.Text("", "Caption");
        destination.Margin = new Thickness(0, 10, 0, 0);
        context.OnRefresh(() => UpdateDestination(context.Settings, destination));

        return new SettingsPane("Learning", "\uE8A5", "Translate the current sentence while writing",
        [
            new SettingsCard("Selected-text reading",
                rows.Toggle("Read Selected Text", "Use the reading shortcut on selected text in a supported app.",
                    s => s.LearningReadingEnabled, (s, v) => s.LearningReadingEnabled = v)
                    .Tagged("reading selected text translation dictionary"),
                rows.Combo("Reading Target Language", "Independent of the writing translation direction.", Languages,
                    s => s.LearningReadingTargetLanguage, (s, v) => s.LearningReadingTargetLanguage = v),
                rows.Labeled("Reading Shortcut", "Configure the selected-text shortcut in Shortcuts.",
                    Ui.Button("Configure shortcut", () => context.Navigate(ShortcutsPane.Build(rows)))),
                Rows.Note("Uses the translation provider or local AI explanation model below; the destination appears before each request. The bundled dictionary is also available. Reading uses only the selected text, without surrounding context. Favorites retain the translation or meaning; saving AI usage details is not yet available.")),
            new SettingsCard("Anki review",
                new SettingsRow("Anki review", "Connection, default deck and saved card templates", review)
                    .Tagged("anki review ankiConnect address API key connection deck note type templates")),
            new SettingsCard("Expression assistance",
                rows.Toggle("Find an Expression While Writing",
                    "Describe what you mean at the caret. Confirm a candidate to insert it.",
                    s => s.ExpressionAssistEnabled, (s, v) => s.ExpressionAssistEnabled = v)
                    .Tagged("expression assistance term phrase meaning writing"),
                ExpressionTrigger(rows),
                rows.Combo("Target Language", "Shared with writing translation for generated expressions.", Languages,
                    s => s.LearningTargetLanguage, (s, v) => s.LearningTargetLanguage = v)
                    .Tagged("expression target language shared translation"),
                rows.Labeled("Expression Shortcut", "Configure a shortcut in Shortcuts.",
                    Ui.Button("Configure shortcut", () => context.Navigate(ShortcutsPane.Build(rows))))
                    .Tagged("expression assistance shortcut keybind"),
                Rows.Note("Uses the local AI explanation model below. Your description, selection and bounded neighboring text stay local; the translation endpoint is not used.")),
            new SettingsCard("Manual sentence preview",
                rows.Toggle("Enable Sentence Translation",
                    "The configured shortcut reads the complete current sentence from a supported field and opens a read-only preview.",
                    s => s.LearningTranslationEnabled, (s, v) => s.LearningTranslationEnabled = v)
                    .Tagged("language learning translation sentence preview manual writing current field"),
                rows.Combo("Translation Provider",
                    "Local uses a separate learning model runtime. Endpoint sends only after the learning endpoint is explicitly enabled.",
                    Backends, s => s.EffectiveLearningBackend, (s, v) => s.LearningBackend = v)
                    .Tagged("translation provider local GGUF model endpoint network learning"),
                SourceLanguages(rows),
                rows.Combo("Target Language", "The language requested from the translator.", Languages,
                    s => s.LearningTargetLanguage, (s, v) => s.LearningTargetLanguage = v)
                    .Tagged("to target language english en chinese"),
                TranslationStyle(rows)),
            new SettingsCard("Local learning model",
                Rows.Custom(new LearningLocalModels(context)))
                .VisibleWhen(s => s.EffectiveLearningBackend == LearningTranslationBackend.Local),
            new SettingsCard("AI explanation model",
                rows.Combo<LearningLanguage?>("Explanation Language", "The language used for meanings and teaching explanations.",
                    new (LearningLanguage? Value, string Label)[] { (null, "App language") }
                        .Concat(Languages.Select(choice => ((LearningLanguage?)choice.Value, choice.Label))).ToArray(),
                    s => s.LearningExplanationLanguage, (s, v) => s.LearningExplanationLanguage = v)
                    .Tagged("AI explanation language tutor meaning"),
                Rows.Custom(new LearningLocalModels(context, LearningModelRole.Explanation))),
            new SettingsCard("Learning endpoint",
                rows.Toggle("Allow the Learning Endpoint",
                    "This separate consent sends a manually requested sentence and its selected translation style to the destination below. It sends no surrounding editor text. Selecting Local never uses this endpoint.",
                    s => s.LearningEndpointEnabled, (s, v) => s.LearningEndpointEnabled = v)
                    .Tagged("endpoint send network cloud privacy remote allow opt in translation"),
                rows.Text("Server URL", "OpenAI-compatible endpoint used only for learning translations.",
                    OpenAICompatibleEndpointConfiguration.DefaultBaseUrl,
                    s => s.LearningEndpointBaseUrl, (s, v) => s.LearningEndpointBaseUrl = v.Trim())
                    .Tagged("endpoint url host ollama local network server"),
                rows.Text("Model", "Model identifier configured on the learning endpoint.", "Enter model identifier",
                    s => s.LearningEndpointModel, (s, v) => s.LearningEndpointModel = v.Trim())
                    .Tagged("endpoint model identifier"),
                rows.Number("Context Tokens", "Context budget frozen into each translation request.",
                    2_048, ModelContextBudget.MaximumEndpointTokens,
                    s => s.LearningContextTokens, (s, v) => s.LearningContextTokens = v)
                    .Tagged("context length tokens budget model"),
                Credentials(context),
                Rows.Custom(destination))
                .VisibleWhen(s => s.EffectiveLearningBackend == LearningTranslationBackend.Endpoint),
        ]);
    }

    private static SettingsRow ExpressionTrigger(Rows rows)
    {
        var context = rows.Context;
        var input = new TextBox { Width = 140, Tag = "??" }.Styled("Input");
        input.OnCommit(text => context.Change(settings => settings.ExpressionAssistTrigger = text));
        var validation = Ui.Text("", "Caption");
        validation.TextWrapping = TextWrapping.Wrap;
        context.OnRefresh(() =>
        {
            var trigger = context.Settings.ExpressionAssistTrigger ?? "??";
            if (!input.IsKeyboardFocused) input.Text = trigger;
            validation.Text = ExpressionAssistPolicy.TriggerError(trigger)
                ?? (trigger.Any(character => character > 0x7F)
                    ? "This Windows version supports ASCII trigger symbols. Use ?? or configure the shortcut." : "");
            validation.Visibility = validation.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        return rows.Stacked("Trigger Symbols", "Type the symbols, then describe what you mean in the expression window.",
            new StackPanel { Children = { input, validation } }).Tagged("expression trigger punctuation symbols ??");
    }

    /// <summary>
    /// Keeps endpoint style preferences in their existing flat keys, while local preferences are
    /// keyed by the selected learning model. The request controller later freezes only the active
    /// backend's effective style into LearningTranslationOptions.
    /// </summary>
    private static SettingsRow TranslationStyle(Rows rows)
    {
        var context = rows.Context;
        var register = new ComboBox { ItemsSource = Registers.Select(choice => choice.Label).ToArray() }
            .Styled("Combo");
        var instruction = new TextBox { Width = 240, Tag = "Describe the register" }.Styled("Input");
        var editBackend = context.Settings.EffectiveLearningBackend;
        var editLocalModel = context.Settings.LearningLocalModel;
        void CaptureEditTarget()
        {
            editBackend = context.Settings.EffectiveLearningBackend;
            editLocalModel = context.Settings.LearningLocalModel;
        }
        void SaveRegister(int selected)
        {
            if (selected < 0 || selected >= Registers.Length) return;
            var current = ReadStyle(context.Settings, editBackend, editLocalModel);
            WriteStyle(context, current with { Register = Registers[selected].Value }, editBackend, editLocalModel);
        }

        register.GotKeyboardFocus += (_, _) => CaptureEditTarget();
        register.DropDownOpened += (_, _) => CaptureEditTarget();
        register.SelectionChanged += (_, _) => SaveRegister(register.SelectedIndex);
        instruction.GotKeyboardFocus += (_, _) => CaptureEditTarget();
        instruction.OnCommit(text =>
        {
            var current = ReadStyle(context.Settings, editBackend, editLocalModel);
            WriteStyle(context, current with { CustomInstruction = text }, editBackend, editLocalModel);
        });
        context.OnRefresh(() =>
        {
            var backend = context.Settings.EffectiveLearningBackend;
            var model = context.Settings.LearningLocalModel;
            var style = ReadStyle(context.Settings, backend, model);
            register.SelectedIndex = Array.FindIndex(Registers, choice => choice.Value == style.Register);
            if (!instruction.IsKeyboardFocused) instruction.Text = style.CustomInstruction;
            instruction.Visibility = style.Register == LearningTranslationRegister.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
            bool localNeedsModel = context.Settings.EffectiveLearningBackend == LearningTranslationBackend.Local
                && string.IsNullOrWhiteSpace(context.Settings.LearningLocalModel);
            register.IsEnabled = !localNeedsModel;
            instruction.IsEnabled = !localNeedsModel;
            if (!instruction.IsKeyboardFocused && !register.IsKeyboardFocused)
            {
                editBackend = backend;
                editLocalModel = model;
            }
        });

        var editor = new StackPanel { Children = { register, instruction } };
        return rows.Stacked("Translation Style",
                "Choose a bounded register preference. Local settings follow the selected learning model; endpoint settings remain independent.", editor)
            .Tagged("natural formal conversational custom register tone style instruction")
            .EnabledWhen(settings => settings.EffectiveLearningBackend == LearningTranslationBackend.Endpoint
                || !string.IsNullOrWhiteSpace(settings.LearningLocalModel));
    }

    private static LearningTranslationStyle ReadStyle(
        AppSettings settings, LearningTranslationBackend backend, string localModel)
    {
        if (backend == LearningTranslationBackend.Local && !string.IsNullOrWhiteSpace(localModel))
        {
            return settings.EffectiveLearningLocalGenerationProfile(localModel).Style;
        }

        if (backend == LearningTranslationBackend.Local) return new LearningTranslationStyle();
        var register = Enum.IsDefined(settings.LearningStyleRegister)
            ? settings.LearningStyleRegister
            : LearningTranslationRegister.Natural;
        return new LearningTranslationStyle(register, settings.LearningCustomInstruction ?? string.Empty).Normalized;
    }

    private static void WriteStyle(
        SettingsContext context,
        LearningTranslationStyle style,
        LearningTranslationBackend backend,
        string localModel)
    {
        var normalized = style.Normalized;
        if (backend == LearningTranslationBackend.Endpoint)
        {
            context.Change(settings =>
            {
                settings.LearningStyleRegister = normalized.Register;
                settings.LearningCustomInstruction = normalized.CustomInstruction;
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(localModel)) return;
        var model = localModel;
        var current = context.Settings.EffectiveLearningLocalGenerationProfile(model);
        context.Change(settings =>
        {
            settings.LearningLocalGenerationProfiles ??= new Dictionary<string, LearningGenerationProfile>(StringComparer.OrdinalIgnoreCase);
            settings.LearningLocalGenerationProfiles[model] = new LearningGenerationProfile
            {
                Style = normalized,
                Sampling = current.Sampling,
                MaximumOutputTokens = current.MaximumOutputTokens,
            };
        });
    }

    /// <summary>
    /// Mirrors the Mac source-language disclosure: one selected language means an explicit
    /// direction, while multiple choices are resolved for each captured sentence. AppSettings
    /// keeps the old singular key as a migration source and persists this full ordered choice.
    /// </summary>
    private static SettingsRow SourceLanguages(Rows rows)
    {
        var context = rows.Context;
        var choices = new StackPanel();
        var checkboxes = new List<(LearningLanguage Language, CheckBox CheckBox)>();
        foreach (var language in LearningLanguages.All)
        {
            var checkbox = new CheckBox
            {
                Content = language.Label(),
                Margin = new Thickness(0, 3, 0, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
            }.Styled("Toggle");
            checkbox.Click += (_, _) => context.Change(settings =>
            {
                var selected = settings.EffectiveLearningSourceLanguages.ToList();
                if (checkbox.IsChecked == true)
                {
                    if (!selected.Contains(language)) selected.Add(language);
                }
                else
                {
                    selected.RemoveAll(candidate => candidate == language);
                }

                settings.LearningSourceLanguages = selected;
                // Keep downgrade compatibility whenever the new choice is representable by the
                // legacy field. A multi-source list stays intact and is never collapsed to one.
                if (selected.Count == 1) settings.LearningSourceLanguage = selected[0];
            });
            choices.Children.Add(checkbox);
            checkboxes.Add((language, checkbox));
        }

        var note = Ui.Text(
            "With one source selected, translations use that direction. Multiple-source detection is not yet available on this Windows build; choose one source to translate.",
            "Caption");
        note.Margin = new Thickness(0, 8, 0, 0);
        note.TextWrapping = TextWrapping.Wrap;
        choices.Children.Add(note);

        var expander = new Expander { IsExpanded = false, Content = choices };
        context.OnRefresh(() =>
        {
            var selected = context.Settings.EffectiveLearningSourceLanguages;
            expander.Header = $"{selected.Count} selected";
            foreach (var (language, checkbox) in checkboxes)
            {
                checkbox.IsChecked = selected.Contains(language);
                checkbox.IsEnabled = language != context.Settings.LearningTargetLanguage;
            }
        });

        return rows.Stacked("Source Languages",
            "Choose one source language to translate. Multiple-source detection is not yet available on this Windows build.", expander)
            .Tagged("source language languages detection automatic chinese english spanish french german japanese korean hindi arabic");
    }

    private static SettingsRow Credentials(SettingsContext context)
    {
        var key = new PasswordBox { Width = 240 }.Styled("PasswordInput");
        var status = Ui.Text("", "Caption");
        var save = Ui.Button("Save", () => StoreKey(context, key, status, save: true));
        var clear = Ui.Button("Clear", () => StoreKey(context, key, status, save: false));
        context.OnRefresh(() =>
        {
            if (context.Settings.EffectiveLearningBackend == LearningTranslationBackend.Endpoint)
                ShowKeyStatus(context, status);
            else
                status.Text = "The separate endpoint credential is not read or used while Local is selected.";
        });
        return new SettingsRow("Learning API Key",
            "Optional bearer token stored separately from autocomplete credentials.",
            new StackPanel { Children = { Ui.Across(key, save, clear), status } });
    }

    private static void StoreKey(SettingsContext context, PasswordBox key, TextBlock status, bool save)
    {
        try
        {
            bool hasKey = save && !string.IsNullOrWhiteSpace(key.Password);
            if (save) context.Host.LearningEndpointCredentials.SaveApiKey(key.Password);
            else context.Host.LearningEndpointCredentials.DeleteApiKey();
            key.Clear();
            context.Host.LearningCredentialsChanged();
            status.Text = hasKey
                ? "A learning API key is stored with Windows user encryption."
                : "No learning API key is stored.";
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            status.Text = "Could not update the learning API key.";
        }
    }

    private static void ShowKeyStatus(SettingsContext context, TextBlock status)
    {
        try
        {
            status.Text = context.Host.LearningEndpointCredentials.ReadApiKey() is null
                ? "No learning API key is stored."
                : "A learning API key is stored with Windows user encryption.";
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            status.Text = "The learning API key could not be read.";
        }
    }

    private static void UpdateDestination(AppSettings settings, TextBlock destination)
    {
        try
        {
            var configuration = new OpenAICompatibleEndpointConfiguration(
                settings.LearningEndpointBaseUrl,
                settings.LearningEndpointModel,
                OpenAICompatibleApiMode.ChatCompletions);
            if (configuration.ModelName.Length == 0)
            {
                    destination.Text = settings.LearningEndpointEnabled
                    ? $"Learning requests will use {configuration.BaseUrl}; enter a model before translating. The captured sentence and selected style are sent."
                    : $"If enabled, the captured sentence and selected style will be sent to {configuration.BaseUrl}.";
            }
            else
            {
                var scope = configuration.HostScope switch
                {
                    OpenAICompatibleHostScope.Loopback => "This endpoint is on this PC.",
                    OpenAICompatibleHostScope.LocalNetwork => "This endpoint is on your local network.",
                    _ => "This endpoint is outside this PC; the captured sentence will leave the device.",
                };
                destination.Text = $"Learning destination: {configuration.BaseUrl} · {configuration.ModelName}. The captured sentence and selected style are sent. {scope}";
            }

            if (settings.LearningEndpointEnabled)
            {
                destination.Text += " The endpoint is opted in for manual sentence requests.";
            }
            else
            {
                destination.Text += " Endpoint sending is off.";
            }
        }
        catch (OpenAICompatibleEndpointException ex)
        {
            destination.Text = ex.Message + " Learning endpoint requests remain unavailable until this is fixed.";
        }
    }
}
