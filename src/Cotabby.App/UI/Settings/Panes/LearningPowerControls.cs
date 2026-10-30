using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.App.Settings;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>Role-scoped power choices. Model discovery is borrowed from the existing owner;
/// this control never loads a model, probes an endpoint, or accesses a credential.</summary>
internal sealed class LearningPowerControls : StackPanel
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<Action<AppSettings>> _change;
    private readonly LearningModelRole _role;
    private readonly CheckBox _enabled = new() { Content = InterfaceText.Get("Switch based on power source", "Localizable") };
    private readonly StackPanel _choices = new();
    private readonly ComboBox[] _backends = [new(), new()];
    private readonly ComboBox[] _models = [new(), new()];
    private readonly ComboBox[] _templates = [new(), new()];
    private readonly TextBox[] _endpoints = [new(), new()];
    private string[] _installed = [];
    private bool _refreshing, _closed;

    public LearningPowerControls(SettingsContext context, LearningModelRole role)
        : this(() => context.Settings, context.Change, role)
    {
        context.OnRefresh(Refresh); context.Closed += () => _closed = true;
        RefreshModels(context);
    }

    // Synthetic settings and model names allow a component render without opening app data.
    internal LearningPowerControls(Func<AppSettings> settings, Action<Action<AppSettings>> change, LearningModelRole role, IEnumerable<string>? installed = null)
    {
        _settings = settings; _change = change; _role = role; _installed = installed?.ToArray() ?? [];
        Children.Add(_enabled);
        var description = Ui.Text(InterfaceText.Get("Use a different engine or model on battery and while plugged in for this task.", "Localizable"), "Caption");
        description.TextWrapping = TextWrapping.Wrap; Children.Add(description);
        Children.Add(_choices);
        for (int index = 0; index < 2; index++)
        {
            int slot = index;
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Ui.Text(InterfaceText.Get(index == 0 ? "On battery" : "Plugged in", "Learning"), "RowLabel"));
            _backends[index].Styled("Combo"); _models[index].Styled("Combo"); _templates[index].Styled("Combo"); _endpoints[index].Styled("Input");
            _backends[index].ItemsSource = new[] { LearningModelBackend.Local, LearningModelBackend.Endpoint };
            _backends[index].ItemTemplate = InterfaceEnumLabels.Create<LearningModelBackend>(value => value == LearningModelBackend.Local ? "Local model" : "Endpoint", "Learning");
            _backends[index].SelectionChanged += (_, _) => EditBackend(slot);
            _models[index].SelectionChanged += (_, _) => EditModel(slot);
            _templates[index].ItemsSource = LearningChatTemplatePolicy.AvailableFor(role).Select(value => new TemplateOption(value, InterfaceText.Get(value.Label(), "Learning"))).ToArray();
            _templates[index].DisplayMemberPath = nameof(TemplateOption.Label); _templates[index].SelectedValuePath = nameof(TemplateOption.Value);
            _templates[index].SelectionChanged += (_, _) => EditTemplate(slot);
            _endpoints[index].LostKeyboardFocus += (_, _) => EditEndpoint(slot);
            var controls = new (string Label, Control Element)[] { ("Engine", _backends[index]), ("Local model", _models[index]),
                ("Instruction template", _templates[index]), ("Endpoint model identifier", _endpoints[index]) };
            foreach (var (label, element) in controls)
            {
                var caption = Ui.Text(InterfaceText.Get(label), "Caption"); caption.Margin = new Thickness(0, 5, 0, 0);
                caption.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = element });
                row.Children.Add(caption); element.Margin = new Thickness(0, 2, 0, 0); row.Children.Add(element);
            }
            System.Windows.Automation.AutomationProperties.SetName(_endpoints[index], InterfaceText.Get(index == 0 ? "On battery" : "Plugged in", "Learning") + " · " + InterfaceText.Get("Endpoint model identifier"));
            System.Windows.Automation.AutomationProperties.SetName(_backends[index], InterfaceText.Get(index == 0 ? "On battery" : "Plugged in", "Learning") + " · " + InterfaceText.Get("Engine"));
            System.Windows.Automation.AutomationProperties.SetName(_models[index], InterfaceText.Get(index == 0 ? "On battery" : "Plugged in", "Learning") + " · " + InterfaceText.Get("Local model"));
            System.Windows.Automation.AutomationProperties.SetName(_templates[index], InterfaceText.Get(index == 0 ? "On battery" : "Plugged in", "Learning") + " · " + InterfaceText.Get("Instruction template"));
            _choices.Children.Add(row);
        }
        var note = Ui.Text(InterfaceText.Get("Endpoint profiles use this task's connection address and credential. Model choices do not change autocomplete or the other learning task.", "Localizable"), "Caption");
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 8, 0, 0); _choices.Children.Add(note);
        _enabled.Checked += (_, _) => SetEnabled(true); _enabled.Unchecked += (_, _) => SetEnabled(false);
        Refresh();
    }

    private bool Enabled => _role == LearningModelRole.Translation ? _settings().LearningTranslationPowerSwitching : _settings().LearningExplanationPowerSwitching;
    private LearningPowerProfile Selection(int slot) => _role == LearningModelRole.Translation
        ? (slot == 0 ? _settings().LearningTranslationBatteryProfile : _settings().LearningTranslationPluggedInProfile) ?? _settings().LearningTranslationManualSelection
        : (slot == 0 ? _settings().LearningExplanationBatteryProfile : _settings().LearningExplanationPluggedInProfile) ?? _settings().LearningExplanationManualSelection;
    private void SetEnabled(bool value) { if (!_refreshing) { _change(s => s.SetLearningPowerSwitching(_role, value)); Refresh(); } }
    private void Set(int slot, LearningPowerProfile value)
    {
        if (_refreshing) return;
        _change(s => { if (_role == LearningModelRole.Translation) { if (slot == 0) s.LearningTranslationBatteryProfile = value; else s.LearningTranslationPluggedInProfile = value; }
            else { if (slot == 0) s.LearningExplanationBatteryProfile = value; else s.LearningExplanationPluggedInProfile = value; } });
        Refresh();
    }
    private void EditBackend(int slot)
    {
        if (_refreshing || _backends[slot].SelectedItem is not LearningModelBackend backend || backend == Selection(slot).Backend) return;
        Set(slot, backend == LearningModelBackend.Endpoint ? LearningPowerProfile.Endpoint("") : LearningPowerProfile.Local(""));
    }
    private void EditModel(int slot)
    {
        if (_refreshing || _models[slot].SelectedItem is not string model) return;
        var current = Selection(slot); if (model == current.LocalModel) return;
        Set(slot, LearningPowerProfile.Local(model));
    }
    private void EditTemplate(int slot)
    {
        if (_refreshing || _templates[slot].SelectedValue is not LearningChatTemplate template) return;
        var current = Selection(slot); if (template != current.Template) Set(slot, current with { Template = template });
    }
    private void EditEndpoint(int slot)
    {
        var current = Selection(slot);
        if (!_refreshing && current.Backend == LearningModelBackend.Endpoint && _endpoints[slot].Text != current.EndpointModel)
            Set(slot, current with { EndpointModel = _endpoints[slot].Text });
    }
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            _enabled.IsChecked = Enabled; _choices.Visibility = Enabled ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < 2; i++)
            {
                var value = Selection(i).ActiveProjection(); _backends[i].SelectedItem = value.Backend;
                _models[i].ItemsSource = new[] { "" }.Concat(_installed).Concat(string.IsNullOrEmpty(value.LocalModel) ? [] : [value.LocalModel]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                _models[i].SelectedItem = value.LocalModel; _templates[i].SelectedValue = value.Template;
                if (!_endpoints[i].IsKeyboardFocused) _endpoints[i].Text = value.EndpointModel;
                bool local = value.Backend == LearningModelBackend.Local;
                _models[i].Visibility = _templates[i].Visibility = local ? Visibility.Visible : Visibility.Collapsed;
                _endpoints[i].Visibility = local ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        finally { _refreshing = false; }
    }
    private async void RefreshModels(SettingsContext context)
    {
        try
        {
            var task = _role == LearningModelRole.Translation ? LocalModelTask.Translation : LocalModelTask.Explanation;
            var models = await context.Host.LearningModels.GetInstalledModelsForTaskAsync(task, context.Settings.ModelAssignmentSnapshot(), CancellationToken.None);
            if (_closed) return; _installed = models.Select(x => x.Filename).ToArray(); Refresh();
        }
        catch (Exception) { if (!_closed) Refresh(); }
    }
    private sealed record TemplateOption(LearningChatTemplate Value, string Label);
}
