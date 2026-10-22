using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
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

    public static SettingsPane Build(Rows rows)
    {
        var context = rows.Context;
        var destination = Ui.Text("", "Caption");
        destination.Margin = new Thickness(0, 10, 0, 0);
        context.OnRefresh(() => UpdateDestination(context.Settings, destination));

        return new SettingsPane("Learning", "\uE8A5", "Translate the current sentence while writing",
        [
            new SettingsCard("Manual sentence preview",
                rows.Toggle("Enable Sentence Translation",
                    "The configured shortcut reads the complete current sentence from a supported field and opens a read-only preview.",
                    s => s.LearningTranslationEnabled, (s, v) => s.LearningTranslationEnabled = v)
                    .Tagged("language learning translation sentence preview manual writing current field"),
                rows.Combo("Source Language", "The language used for the captured sentence.", Languages,
                    s => s.LearningSourceLanguage, (s, v) => s.LearningSourceLanguage = v)
                    .Tagged("from source language chinese zh english"),
                rows.Combo("Target Language", "The language requested from the translator.", Languages,
                    s => s.LearningTargetLanguage, (s, v) => s.LearningTargetLanguage = v)
                    .Tagged("to target language english en chinese"),
                rows.Combo("Translation Style", "Choose the register added to the translation prompt.", Registers,
                    s => s.LearningStyleRegister, (s, v) => s.LearningStyleRegister = v)
                    .Tagged("natural formal conversational custom register tone style"),
                rows.Text("Custom Style Instruction",
                    "A bounded style preference used only when Custom is selected.",
                    "Describe the register", s => s.LearningCustomInstruction,
                    (s, v) => s.LearningCustomInstruction = v)
                    .VisibleWhen(s => s.LearningStyleRegister == LearningTranslationRegister.Custom)
                    .Tagged("custom instruction register style tone")),
            new SettingsCard("Learning endpoint",
                rows.Toggle("Allow the Learning Endpoint",
                    "This separate consent sends a manually requested sentence and its selected translation style to the destination below. It sends no surrounding editor text. No local translation provider is configured in this Windows slice.",
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
                Rows.Custom(destination)),
        ]);
    }

    private static SettingsRow Credentials(SettingsContext context)
    {
        var key = new PasswordBox { Width = 240 }.Styled("PasswordInput");
        var status = Ui.Text("", "Caption");
        var save = Ui.Button("Save", () => StoreKey(context, key, status, save: true));
        var clear = Ui.Button("Clear", () => StoreKey(context, key, status, save: false));
        context.OnRefresh(() => ShowKeyStatus(context, status));
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
