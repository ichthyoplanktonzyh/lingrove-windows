using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Hosting;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Models;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI;

/// <summary>Presentation only: the AppHost owner retains transfers and the pending receipt.</summary>
internal sealed class RecommendedSetupView : UserControl
{
    private readonly RecommendedSetupController _owner;
    private readonly Func<AppSettings> _settings;
    private readonly TextBlock _completion = new(), _translation = new(), _explanation = new(), _endpoint = new(), _size = new();
    private readonly Button _apply = new() { Content = InterfaceText.Get("Use recommended setup"), Padding = new Thickness(12, 6, 12, 6) };
    private readonly Button _cancel = new() { Content = "Cancel downloads", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
    internal event Action? ManualRequested;
    internal event Action? Applied;
    internal RecommendedSetupView(AppHost host) : this(host.RecommendedSetup, () => host.Settings) { }
    internal RecommendedSetupView(RecommendedSetupController owner, Func<AppSettings> settings)
    {
        _owner = owner; _settings = settings;
        Background = SystemColors.WindowBrush; Foreground = SystemColors.WindowTextBrush;
        var body = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Recommended setup"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Set up local autocomplete, sentence translation and explanations together. Existing models, languages and shortcuts are kept.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12), Foreground = SystemColors.GrayTextBrush });
        AddRow(body, "Autocomplete", _completion); AddRow(body, "Translation", _translation); AddRow(body, "AI explanation", _explanation);
        AddRow(body, "Read aloud", new TextBlock { Text = "Not available in this Windows preview", Foreground = SystemColors.GrayTextBrush });
        body.Children.Add(new TextBlock { Text = "Models download only when you choose setup. Translation uses the manual sentence shortcut; automatic expression assistance is still in development.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 8, 0, 4) });
        _endpoint.TextWrapping = TextWrapping.Wrap; _endpoint.Foreground = SystemColors.GrayTextBrush; _endpoint.Margin = new Thickness(0, 4, 0, 8); body.Children.Add(_endpoint);
        _size.TextWrapping = TextWrapping.Wrap; _size.Margin = new Thickness(0, 2, 0, 4); body.Children.Add(_size);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _apply.Click += (_, _) => { if (!owner.NeedsApplication) owner.Retry(); else { owner.Apply(); Applied?.Invoke(); } };
        _cancel.Click += (_, _) => owner.Cancel();
        var manual = new Button { Content = InterfaceText.Get("Configure manually"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        manual.Click += (_, _) => { owner.UseManualConfiguration(); ManualRequested?.Invoke(); };
        actions.Children.Add(_apply); actions.Children.Add(_cancel); actions.Children.Add(manual); body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => { owner.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => owner.Changed -= Refresh;
        Refresh();
    }
    private static void AddRow(Panel body, string title, TextBlock detail)
    {
        var row = new StackPanel { Margin = new Thickness(0, 4, 0, 5) };
        row.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0, 2, 0, 0); row.Children.Add(detail); body.Children.Add(row);
    }
    private void Refresh()
    {
        var owner = _owner; var s = _settings();
        string? completionId = s.RecommendedCompletionTarget ?? s.ActiveModelId;
        string completionFile = AppSettings.CompletionFilenameForId(completionId) ?? (string.IsNullOrEmpty(completionId) ? owner.Plan.Completion.FileName : "");
        string completion = s.Engine == EngineKind.Endpoint ? "Existing endpoint: " + ModelDisplayText.Safe(s.EndpointModel) : s.PowerModelSwitching ? "Existing power profiles" :
            completionFile.Length == 0 ? "Existing model: " + ModelDisplayText.Safe(completionId) : ModelDisplayText.Name(completionFile);
        string translationFile = string.IsNullOrEmpty(s.LearningLocalModel) ? owner.Plan.Translation.Filename : s.LearningLocalModel;
        string translation = s.EffectiveLearningBackend == LearningTranslationBackend.Endpoint ? "Existing endpoint: " + ModelDisplayText.Safe(s.LearningEndpointModel) :
            ModelDisplayText.Name(translationFile);
        string explanationFile = string.IsNullOrEmpty(s.LearningExplanationLocalModel) ? owner.Plan.Explanation.Filename : s.LearningExplanationLocalModel;
        string explanation = ModelDisplayText.Name(explanationFile);
        _completion.Text = completion + (s.Engine == EngineKind.Local && !s.PowerModelSwitching ? " · " + owner.Status(LocalModelTask.Completion, completionFile) : "");
        _translation.Text = translation + (s.EffectiveLearningBackend == LearningTranslationBackend.Local ? " · " + owner.Status(LocalModelTask.Translation, translationFile) : "");
        _explanation.Text = explanation + " · " + owner.Status(LocalModelTask.Explanation, explanationFile);
        var destinations = new List<string>();
        if (s.Engine == EngineKind.Endpoint) destinations.Add("Autocomplete: " + ModelDisplayText.Safe(s.EndpointBaseUrl));
        if (s.EffectiveLearningBackend == LearningTranslationBackend.Endpoint) destinations.Add("Translation: " + ModelDisplayText.Safe(s.LearningEndpointBaseUrl));
        _endpoint.Text = destinations.Count > 0 ? "Enabled tasks send requests to: " + string.Join("; ", destinations) : "Local models are stored on this PC.";
        var completionModel = ModelCatalog.All.FirstOrDefault(model => string.Equals(model.FileName, completionFile, StringComparison.OrdinalIgnoreCase));
        var translationModel = LearningRuntimeCatalog.TranslationModels.FirstOrDefault(model => string.Equals(model.Filename, translationFile, StringComparison.OrdinalIgnoreCase));
        var explanationModel = LearningRuntimeCatalog.ExplanationModels.FirstOrDefault(model => string.Equals(model.Filename, explanationFile, StringComparison.OrdinalIgnoreCase));
        double maximum = (s.Engine == EngineKind.Local && !s.PowerModelSwitching ? completionModel?.ApproxSizeBytes ?? 0 : 0)
            + (s.EffectiveLearningBackend == LearningTranslationBackend.Local ? translationModel?.ExpectedSizeBytes ?? 0 : 0)
            + (explanationModel?.ExpectedSizeBytes ?? 0);
        _size.Text = maximum > 0 ? $"Recommended downloads: up to {maximum / 1_000_000_000:0.0} GB (less if already installed)." : "";
        _apply.Content = owner.NeedsApplication ? "Use recommended setup" : "Retry downloads"; _apply.IsEnabled = !owner.Busy; _cancel.IsEnabled = owner.Busy;
    }
}
