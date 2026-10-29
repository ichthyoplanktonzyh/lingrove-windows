using System.IO;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Models;

namespace Cotabby.App.UI.Settings.Panes;

internal static class EnginePane
{
    public static SettingsPane Build(Rows r)
    {
        var context = r.Context;
        ModelManagementWindow? manager = null;
        void ManageModels()
        {
            if (manager is not null) { manager.Activate(); return; }
            var note = new TextBlock { Text = "Use Download Models in the engine pane to get recommended autocomplete models.", TextWrapping = TextWrapping.Wrap };
            var window = new ModelManagementWindow(context, LocalModelTask.Completion, note, null, context.Refresh);
            window.Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            manager = window;
            window.Closed += (_, _) => manager = null;
            window.ShowDialog();
        }
        context.Closed += () => manager?.Close();
        var runtimeStatus = new StatusLine();
        context.OnRefresh(() =>
        {
            var active = context.Host.AdmittedLocalModel;
            runtimeStatus.Set(active is not null, active is not null ? $"Ready · {active.DisplayName}" : "Not loaded");
        });

        return new SettingsPane("Engine & Model", "\uE950", "Choose the engine and models",
        [
            new SettingsCard("Engine",
                r.Combo("Engine",
                    "Local model runs a downloaded model file on this PC. "
                    + "Endpoint connects to an OpenAI-compatible server you manage.",
                    [(EngineKind.Local, "Local model"), (EngineKind.Endpoint, "OpenAI-compatible endpoint")],
                    s => s.Engine, (s, v) => s.Engine = v)
                    .Tagged("backend provider runtime local ollama openai compatible lm studio vllm on-device")),
            new SettingsCard("Power",
                r.Toggle("Switch Based on Power Source",
                    "Use a different local model on battery and while plugged in. "
                    + "For example, a small model on battery to save power and a larger one while charging.",
                    s => s.PowerModelSwitching, (s, v) => s.PowerModelSwitching = v)
                    .Tagged("battery plugged energy ac charger adaptive auto switch"),
                r.Labeled("On Battery", "Local model to use while on battery.", PowerPicker(context,
                    s => s.BatteryModelId, (s, v) => s.BatteryModelId = v))
                    .Tagged("unplugged low power energy saver model")
                    .VisibleWhen(s => s.PowerModelSwitching),
                r.Labeled("Plugged In", "Local model to use while plugged in.", PowerPicker(context,
                    s => s.PluggedInModelId, (s, v) => s.PluggedInModelId = v))
                    .Tagged("ac charger wall power wired model")
                    .VisibleWhen(s => s.PowerModelSwitching)),
            .. Local(
                new SettingsCard("Status",
                    r.Labeled("Model Status", "Whether the local model is loaded and ready.", runtimeStatus)
                        .Tagged("loaded ready runtime running health loading")),
                new SettingsCard("Models",
                    r.Action("Manage models", "Select installed models and assign custom files to autocomplete.", "Manage models", ManageModels),
                    r.Stacked("Download Models",
                        "Curated models you can download and run. Click a downloaded model to switch to it.",
                        new ModelList(context))
                        .Tagged("selected active choose current default catalog fetch install get library")),
                new SettingsCard("Folder", ModelsFolder(r))),
            .. new EndpointSection(r).Cards().Select(card => card.VisibleWhen(s => s.Engine == EngineKind.Endpoint)),
        ]);
    }

    /// <summary>The row that shows the models folder. The Advanced pane lists it too.</summary>
    public static SettingsRow ModelsFolder(Rows r) =>
        r.Action("Models Folder", ModelCatalog.DefaultLocalDirectory(), "Open Folder", () =>
        {
            var folder = ModelCatalog.DefaultLocalDirectory();
            Directory.CreateDirectory(folder);
            Ui.OpenPath(folder);
        })
        .Tagged("path directory open reveal location storage downloaded files");

    private static IEnumerable<SettingsCard> Local(params SettingsCard[] cards) =>
        cards.Select(card => card.VisibleWhen(s => s.Engine == EngineKind.Local));

    private static ComboBox PowerPicker(SettingsContext context, Func<AppSettings, string> get, Action<AppSettings, string> set)
    {
        var combo = new ComboBox { MinWidth = 200, DisplayMemberPath = nameof(PowerChoice.Label), SelectedValuePath = nameof(PowerChoice.Id) }.Styled("Combo");
        bool updating = false, closed = false; int generation = 0;
        combo.ItemContainerStyle = new Style(typeof(ComboBoxItem), combo.ItemContainerStyle)
        { Setters = { new Setter(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(PowerChoice.Selectable))) } };
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedItem is PowerChoice { Selectable: true } choice && choice.Id != get(context.Settings))
                context.Change(settings => set(settings, choice.Id));
        };
        async void Refresh()
        {
            int current = ++generation;
            var selected = get(context.Settings) ?? "";
            var assignments = context.Settings.ModelAssignmentSnapshot();
            IReadOnlyList<CotabbyModel> models;
            try { models = await System.Threading.Tasks.Task.Run(() => ModelCatalog.Available(assignments)); }
            catch (Exception) { models = []; }
            if (closed || current != generation) return;
            var choices = PowerChoices(models, selected);
            updating = true;
            try { combo.ItemsSource = choices; combo.SelectedValue = selected; }
            finally { updating = false; }
        }
        context.OnRefresh(Refresh);
        context.Closed += () => { closed = true; generation++; };
        return combo;
    }
    internal static IReadOnlyList<PowerChoice> PowerChoices(IReadOnlyList<CotabbyModel> models, string selected)
    {
        var choices = new List<PowerChoice> { new("", "Keep current model", true) };
        choices.AddRange(models.Select(model => new PowerChoice(ModelCatalog.MatchesId(selected, model.Id) ? selected : model.Id,
            ModelDisplayText.Safe(model.DisplayName), true)));
        if (selected.Length > 0 && !choices.Any(choice => choice.Id == selected))
        {
            var filename = AppSettings.CompletionFilenameForId(selected) ?? selected;
            choices.Add(new(selected, ModelDisplayText.Unavailable(filename, LocalModelTask.Completion), false));
        }
        return choices;
    }
    internal sealed record PowerChoice(string Id, string Label, bool Selectable);
}
