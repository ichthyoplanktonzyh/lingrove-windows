using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>Settings-only view for the independent explanation destination. Metadata work is
/// owned by the feature through its UI continuation; credentials are touched only on explicit actions.</summary>
internal sealed class LearningExplanationProviderSettings : StackPanel
{
    private readonly Func<AppSettings> _current; private readonly Action<Action<AppSettings>> _change;
    private readonly Func<Func<CancellationToken, Task>, Task> _run;
    private readonly Func<LearningModelSnapshot, CancellationToken, Task<IReadOnlyList<OpenAICompatibleModelOption>>> _fetch;
    private readonly Func<string?, bool> _saveKey; private readonly Action _keyChanged;
    private CancellationTokenSource? _request;
    private long _revision; private bool _updating, _disposed; private LearningModelSnapshot? _displayed;
    internal ComboBox Backend { get; } = new() { MinWidth = 220 };
    internal TextBox Url { get; } = new() { MinWidth = 230 };
    internal TextBox Model { get; } = new() { MinWidth = 180 };
    internal ComboBox Models { get; } = new() { MinWidth = 120 };
    internal PasswordBox Key { get; } = new() { MinWidth = 180 };
    internal TextBlock Destination { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBox ContextTokens { get; } = new() { Width = 90 };
    internal TextBox OutputTokens { get; } = new() { Width = 90 };
    internal TextBox Temperature { get; } = new() { Width = 90, Tag = "Recommended" };
    internal TextBox TopP { get; } = new() { Width = 90, Tag = "Recommended" };
    internal Button RefreshModels { get; }
    private readonly FrameworkElement _local; private readonly StackPanel _endpoint = new();

    internal LearningExplanationProviderSettings(SettingsContext context) : this(
        () => context.Settings, context.Change, context.Host.ExplanationEndpoint.RunAsync,
        context.Host.ExplanationEndpoint.FetchModelsAsync, context.Host.ExplanationEndpoint.SaveCredential,
        context.Host.ExplanationCredentialsChanged, new LearningLocalModels(context, LearningModelRole.Explanation))
    { Children.Add(new LearningPowerControls(context, LearningModelRole.Explanation)); context.OnRefresh(Refresh); context.Closed += Close; }

    internal LearningExplanationProviderSettings(Func<AppSettings> current, Action<Action<AppSettings>> change,
        Func<Func<CancellationToken, Task>, Task> run,
        Func<LearningModelSnapshot, CancellationToken, Task<IReadOnlyList<OpenAICompatibleModelOption>>> fetch,
        Func<string?, bool> saveKey, Action keyChanged, FrameworkElement local)
    {
        _current = current; _change = change; _run = run; _fetch = fetch; _saveKey = saveKey; _keyChanged = keyChanged; _local = local;
        Backend.Styled("Combo"); Models.Styled("Combo"); Key.Styled("PasswordInput");
        foreach (var box in new[] { Url, Model, ContextTokens, OutputTokens, Temperature, TopP }) box.Styled("Input");
        void Name(DependencyObject control, string label) => AutomationProperties.SetName(control, InterfaceText.Get("AI explanation", "Learning") + " · " + InterfaceText.Get(label));
        Name(Backend, "Run using"); Name(Url, "Server URL"); Name(Model, "Endpoint model identifier"); Name(Models, "Models found on the server");
        Name(Key, "API Key"); Name(ContextTokens, "Context budget"); Name(OutputTokens, "Output length"); Name(Temperature, "Temperature"); Name(TopP, "Top P");
        Backend.ItemsSource = new[] { InterfaceText.Get("Local model"), InterfaceText.Get("OpenAI-compatible endpoint") }; Backend.SelectionChanged += (_, _) =>
        { if (!_updating && !_current().LearningExplanationPowerSwitching && Backend.SelectedIndex >= 0) Mutate(s => s.LearningExplanationBackend = Backend.SelectedIndex == 1 ? LearningModelBackend.Endpoint : LearningModelBackend.Local); };
        RefreshModels = Ui.Button(InterfaceText.Get("Test / Refresh models", "Localizable"), BeginRefresh);
        Children.Add(Line("Run using", Backend)); Children.Add(Destination); Children.Add(_local); Children.Add(_endpoint);
        _endpoint.Children.Add(Line("Server URL", Url)); _endpoint.Children.Add(Line("Model", Model, Models));
        _endpoint.Children.Add(Line("Connection", RefreshModels)); _endpoint.Children.Add(Status);
        _endpoint.Children.Add(new TextBlock { Text = InterfaceText.Get("With this endpoint, the service receives the chosen text or expression description, bounded surrounding text, and the requested languages for explanation or expression assistance.", "Learning"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) });
        _endpoint.Children.Add(Line("API key", Key, Ui.Button(InterfaceText.Get("Save", "Localizable"), () => StoreKey(Key.Password)), Ui.Button(InterfaceText.Get("Clear", "Localizable"), () => StoreKey(null))));
        _endpoint.Children.Add(Line("Context tokens", ContextTokens)); _endpoint.Children.Add(Line("Output tokens", OutputTokens));
        _endpoint.Children.Add(Line("Temperature", Temperature)); _endpoint.Children.Add(Line("Top P", TopP));
        _endpoint.Children.Add(new TextBlock { Text = InterfaceText.Get("Leave sampling fields empty to use recommended values.", "Localizable"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4) });
        BindDestinationText(Url, (s, text) => s.LearningExplanationEndpointBaseUrl = text.Trim(), manualOnly: false);
        BindDestinationText(Model, (s, text) => s.LearningExplanationEndpointModel = text.Trim(), manualOnly: true);
        Models.SelectionChanged += (_, _) => { if (!_updating && !_current().LearningExplanationPowerSwitching && Models.SelectedItem is string model) Mutate(s => s.LearningExplanationEndpointModel = model); };
        BindNumber(ContextTokens, (s, value) => { if (int.TryParse(value, out int n)) s.LearningExplanationEndpointContextTokensByDestination[s.LearningExplanationEndpointIdentity] = Math.Clamp(n, 1024, ModelContextBudget.MaximumEndpointTokens); });
        BindNumber(OutputTokens, (s, value) => { if (int.TryParse(value, out int n)) UpdateGeneration(s, p => p with { MaximumOutputTokens = LearningExplanationModelPolicy.ResolveOutputTokens(n) }); });
        BindNumber(Temperature, (s, value) => { if (TryOptional(value, out var n)) UpdateGeneration(s, p => p with { Sampling = p.Sampling with { Temperature = n } }); });
        BindNumber(TopP, (s, value) => { if (TryOptional(value, out var n)) UpdateGeneration(s, p => p with { Sampling = p.Sampling with { TopP = n } }); });
        Refresh();
    }
    private static FrameworkElement Line(string label, params FrameworkElement[] values)
    {
        var row = new Grid { Margin = new(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = InterfaceText.Get(label), VerticalAlignment = VerticalAlignment.Top });
        var panel = new WrapPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(panel, 1);
        foreach (var value in values) { value.Margin = new(0, 0, 6, 4); panel.Children.Add(value); }
        row.Children.Add(panel); return row;
    }
    private void Mutate(Action<AppSettings> edit) { if (_disposed) return; _change(edit); Refresh(); }
    private static bool TryOptional(string text, out double? result)
    {
        result = null; if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number)) return false;
        result = number; return true;
    }
    private static void UpdateGeneration(AppSettings s, Func<LearningGenerationProfile, LearningGenerationProfile> edit)
    {
        var current = s.EffectiveLearningExplanationEndpointGenerationProfile;
        var next = edit(current); s.LearningExplanationEndpointGenerationProfiles[s.LearningExplanationEndpointIdentity] = next with { Sampling = next.Sampling.EndpointOnly };
    }
    private void BindNumber(TextBox box, Action<AppSettings, string> edit)
    {
        string? identity = null;
        box.GotKeyboardFocus += (_, _) => identity = _current().LearningExplanationEndpointIdentity;
        box.OnCommit(value => { string? captured = identity; identity = null; if (captured is not null && _current().LearningExplanationActiveSelection.Backend == LearningModelBackend.Endpoint && captured == _current().LearningExplanationEndpointIdentity) Mutate(s => edit(s, value)); else Refresh();
            if (box.IsKeyboardFocusWithin) identity = _current().LearningExplanationEndpointIdentity; });
    }
    private void BindDestinationText(TextBox box, Action<AppSettings, string> edit, bool manualOnly)
    {
        string? identity = null;
        box.GotKeyboardFocus += (_, _) => identity = _current().LearningExplanationEndpointIdentity;
        box.OnCommit(value => { string? captured = identity; identity = null;
            if (captured is not null && _current().LearningExplanationActiveSelection.Backend == LearningModelBackend.Endpoint
                && (!manualOnly || !_current().LearningExplanationPowerSwitching) && captured == _current().LearningExplanationEndpointIdentity)
                Mutate(s => edit(s, value)); else Refresh();
            if (box.IsKeyboardFocusWithin) identity = _current().LearningExplanationEndpointIdentity; });
    }
    internal void Refresh()
    {
        if (_disposed) return; var settings = _current(); var snapshot = settings.LearningExplanationSnapshot();
        if (_displayed is not null && _displayed != snapshot)
        {
            if (_displayed.Backend != snapshot.Backend || _displayed.BaseUrl != snapshot.BaseUrl || _displayed.EndpointModel != snapshot.EndpointModel) Key.Clear();
            Retire();
        }
        _displayed = snapshot;
        _updating = true;
        try
        {
            bool endpoint = snapshot.Backend == LearningModelBackend.Endpoint;
            Backend.SelectedIndex = endpoint ? 1 : 0; Backend.IsEnabled = !settings.LearningExplanationPowerSwitching;
            Model.IsEnabled = Models.IsEnabled = !settings.LearningExplanationPowerSwitching;
            _local.Visibility = endpoint ? Visibility.Collapsed : Visibility.Visible;
            _endpoint.Visibility = endpoint ? Visibility.Visible : Visibility.Collapsed;
            // Inspecting an address before choosing a model does not admit generation.
            Destination.Text = LearningModelDestinationText.Explanation(snapshot, allowEmptyModel: true);
            Set(Url, settings.LearningExplanationEndpointBaseUrl); Set(Model, snapshot.EndpointModel);
            Set(ContextTokens, settings.EffectiveLearningExplanationEndpointContextTokens.ToString(CultureInfo.InvariantCulture));
            var generation = settings.EffectiveLearningExplanationEndpointGenerationProfile;
            Set(OutputTokens, (generation.MaximumOutputTokens ?? 1024).ToString(CultureInfo.InvariantCulture));
            Set(Temperature, generation.Sampling.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "");
            Set(TopP, generation.Sampling.TopP?.ToString(CultureInfo.InvariantCulture) ?? "");
        }
        finally { _updating = false; }
    }
    private static void Set(TextBox box, string value) { if (!box.IsKeyboardFocusWithin) box.Text = value; }
    internal void StoreKey(string? value)
    {
        if (_disposed || _current().LearningExplanationActiveSelection.Backend != LearningModelBackend.Endpoint) return;
        try { bool changed = _saveKey(value); Key.Clear(); if (changed) { _keyChanged(); Retire(); } Status.Text = value is null ? InterfaceText.Get("Key cleared.", "Localizable") : InterfaceText.Get("Key saved.", "Localizable"); Refresh(); }
        catch { Status.Text = InterfaceText.Get("Unable to update the explanation key.", "Localizable"); }
    }
    internal void BeginRefresh()
    {
        if (_disposed || _current().LearningExplanationActiveSelection.Backend != LearningModelBackend.Endpoint) return;
        Retire(); var request = new CancellationTokenSource(); _request = request;
        var snapshot = _current().LearningExplanationSnapshot(); long revision = _revision;
        Status.Text = InterfaceText.Get("Connecting…", "Localizable"); RefreshModels.IsEnabled = false;
        try { _ = Observe(_run(async shutdown =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, shutdown);
            try
            {
                var result = await _fetch(snapshot, linked.Token);
                if (!Current(revision, snapshot, linked.Token)) return;
                _updating = true; try { Models.ItemsSource = result.Select(x => x.Id).ToArray(); Models.SelectedItem = snapshot.EndpointModel; } finally { _updating = false; }
                Status.Text = result.Count == 0 ? InterfaceText.Get("Connected. No models were returned.", "Localizable") : InterfaceText.Get("Connected. Choose a model or enter one manually.", "Localizable");
            }
            catch (OperationCanceledException) { }
            catch { if (Current(revision, snapshot, linked.Token)) Status.Text = InterfaceText.Get("Unable to connect to the explanation endpoint.", "Localizable"); }
            finally { if (Current(revision, snapshot, linked.Token)) RefreshModels.IsEnabled = true; }
        })); }
        catch { Status.Text = InterfaceText.Get("Explanation endpoint settings are unavailable.", "Localizable"); RefreshModels.IsEnabled = true; }
    }
    private bool Current(long revision, LearningModelSnapshot snapshot, CancellationToken token) => !_disposed && !token.IsCancellationRequested
        && revision == _revision && _current().LearningExplanationSnapshot() == snapshot;
    private static async Task Observe(Task work) { try { await work; } catch { } }
    private void Retire() { ++_revision; var old = _request; _request = null; if (old is not null) _ = CancelAndDisposeAsync(old); RefreshModels.IsEnabled = true; Models.ItemsSource = null; Status.Text = InterfaceText.Get("Not connected.", "Localizable"); }
    private static async Task CancelAndDisposeAsync(CancellationTokenSource source) { try { await source.CancelAsync(); } catch { } finally { source.Dispose(); } }
    internal void Close() { if (_disposed) return; _disposed = true; Retire(); Key.Clear(); }
}
