using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Cotabby.App.Learning;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>
/// Presents learning's own installed-model selection and explicit catalog/import actions. It owns
/// only transient WPF controls and file-transfer cancellation; AppHost's model-file manager and
/// lazy inference runtime remain the long-lived side-effect owners.
/// </summary>
/// <remarks>
/// The installed list is read on a worker because a folder may contain multi-gigabyte files and
/// many user imports. Choosing a file changes only learning settings; weights are opened later by
/// its role's runtime after an explicit request. Translation and explanation reuse file-transfer
/// infrastructure and this view, while selections, context and output preferences remain separate.
/// </remarks>
internal sealed class LearningLocalModels : StackPanel
{
    private readonly SettingsContext _context;
    private readonly LearningModelRole _role;
    private readonly ComboBox _installed = new() { MinWidth = 260 };
    private readonly ComboBox _template = new() { MinWidth = 260 };
    private readonly TextBox _contextTokens = new() { Width = 78, TextAlignment = TextAlignment.Right };
    private readonly TextBox _outputTokens = new() { Width = 78, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _installedStatus = Ui.Text("", "Caption");
    private readonly Button _cancelTransfer;
    private readonly StackPanel _catalog = new();
    private readonly CancellationTokenSource _lifetime = new();
    private LearningInstalledModel[] _models = [];
    private CancellationTokenSource? _activeTransfer;
    private string _contextModelBeingEdited = string.Empty;
    private string _outputModelBeingEdited = string.Empty;
    private bool _refreshing;
    private bool _busy;
    private int _refreshGeneration;

    public LearningLocalModels(SettingsContext context, LearningModelRole role = LearningModelRole.Translation)
    {
        _context = context;
        _role = role;
        Margin = new Thickness(0, 4, 0, 0);
        _cancelTransfer = Ui.Button("Cancel", CancelActiveTransfer);
        _installed.Styled("Combo");
        _template.Styled("Combo");
        _contextTokens.Styled("Input");
        _template.ItemsSource = LearningChatTemplatePolicy.AvailableFor(role)
            .Select(template => new TemplateChoice(template, template.Label())).ToArray();
        _template.DisplayMemberPath = "Label";
        _template.SelectedValuePath = "Value";
        _installed.SelectedValuePath = "Value";
        _installed.ItemContainerStyle = new Style(typeof(ComboBoxItem), _installed.ItemContainerStyle)
        {
            Setters = { new Setter(IsEnabledProperty, new Binding(nameof(ModelChoice.IsSelectable))) },
        };

        Children.Add(SettingLine("Installed model", _installed, Ui.Button("Refresh", RefreshInstalledAsync)));
        Children.Add(SettingLine("Instruction template", _template));
        Children.Add(SettingLine("Context tokens", _contextTokens));
        if (role == LearningModelRole.Explanation)
        {
            _outputTokens.Styled("Input");
            Children.Add(SettingLine("Output tokens", _outputTokens));
            _outputTokens.GotKeyboardFocus += (_, _) => _outputModelBeingEdited = SelectedModel;
            _outputTokens.OnCommit(CommitOutputTokens);
        }
        Children.Add(Ui.Across(_installedStatus, _cancelTransfer));

        var folderActions = Ui.Across(
            Ui.Button("Open Models Folder", OpenModelsFolder),
            Ui.Button("Add Your Own .gguf", ImportModels));
        folderActions.Margin = new Thickness(0, 8, 0, 4);
        Children.Add(folderActions);

        var deferredNote = Ui.Text(
            role == LearningModelRole.Translation
                ? "Local translation runs on the CPU and uses each model family's recommended generation settings. Use Add Your Own to import a GGUF into this folder."
                : "AI explanation uses its own general instruction model. It loads on request and releases memory after a minute of inactivity.",
            "Caption");
        deferredNote.TextWrapping = TextWrapping.Wrap;
        deferredNote.Margin = new Thickness(0, 4, 0, 8);
        Children.Add(deferredNote);

        var catalogHeading = Ui.Text("Recommended models", "RowLabel");
        catalogHeading.Margin = new Thickness(0, 10, 0, 4);
        Children.Add(catalogHeading);
        Children.Add(_catalog);
        BuildCatalog();

        _installed.SelectionChanged += OnInstalledSelectionChanged;
        _template.SelectionChanged += OnTemplateSelectionChanged;
        _contextTokens.GotKeyboardFocus += (_, _) => _contextModelBeingEdited = SelectedModel;
        _contextTokens.OnCommit(CommitContextTokens);
        _context.OnRefresh(UpdateControls);
        _context.Closed += CancelTransfers;
        RefreshInstalledAsync();
    }

    private string SelectedModel => _role == LearningModelRole.Translation
        ? _context.Settings.LearningLocalModel : _context.Settings.LearningExplanationLocalModel;
    private LearningChatTemplate SelectedTemplate => _role == LearningModelRole.Translation
        ? _context.Settings.LearningLocalTemplate : _context.Settings.LearningExplanationLocalTemplate;
    private IReadOnlyList<LearningDownloadableModel> Catalog => LearningRuntimeCatalog.ModelsFor(_role);

    private static FrameworkElement SettingLine(string title, params FrameworkElement[] controls)
    {
        var label = Ui.Text(title, "RowLabel");
        label.Width = 140;
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { label } };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(row.Children.Count == 0 ? 0 : 8, 0, 0, 0);
            control.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(control);
        }
        row.Margin = new Thickness(0, 5, 0, 5);
        return row;
    }

    private void BuildCatalog()
    {
        _catalog.Children.Clear();
        foreach (var model in Catalog)
        {
            var status = Ui.Text($"{model.ApproximateSizeInGigabytes:0.00} GB", "Caption");
            status.TextWrapping = TextWrapping.Wrap;
            var get = Ui.Button("Get", () => _ = DownloadModelAsync(model, status));
            var title = new TextBlock { Text = model.DisplayName, FontWeight = FontWeights.SemiBold };
            var description = Ui.Text(model.Recommendation ?? model.Filename, "Caption");
            description.TextWrapping = TextWrapping.Wrap;
            var details = new StackPanel { Children = { title, description, status } };
            Grid.SetColumn(details, 0);
            Grid.SetColumn(get, 1);
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5), Children = { details, get } };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _catalog.Children.Add(new Border
            {
                Padding = new Thickness(8),
                Margin = new Thickness(0, 3, 0, 3),
                Child = grid,
            }.Themed(Border.BackgroundProperty, "InputBg"));
        }
    }

    private void UpdateControls()
    {
        _refreshing = true;
        try
        {
            var settings = _context.Settings;
            _installed.Items.Clear();
            // Mac's empty picker sentinel clears only the role selection, retaining installed
            // files and per-model preferences for later reuse.
            _installed.Items.Add(new ModelChoice("", "Choose a model", true));
            var modelName = SelectedModel;
            if (!string.IsNullOrEmpty(modelName)
                && !_models.Any(model => string.Equals(model.Filename, modelName, StringComparison.OrdinalIgnoreCase)))
            {
                _installed.Items.Add(Choice(modelName, $"{modelName} (file unavailable)"));
            }
            foreach (var model in _models)
                _installed.Items.Add(Choice(model.Filename, $"{model.DisplayName} · {model.SizeBytes / 1_000_000_000d:0.0} GB"));
            _installed.SelectedValue = modelName;
            _template.SelectedValue = Enum.IsDefined(SelectedTemplate)
                ? SelectedTemplate
                : LearningChatTemplate.Automatic;

            var context = _role == LearningModelRole.Translation ? settings.EffectiveLearningLocalContextTokens(modelName)
                : settings.EffectiveLearningExplanationContextTokens(modelName);
            if (!_contextTokens.IsKeyboardFocused)
                _contextTokens.Text = context.ToString(System.Globalization.CultureInfo.CurrentCulture);
            if (!_outputTokens.IsKeyboardFocused)
                _outputTokens.Text = LearningExplanationModelPolicy.ResolveOutputTokens(
                    settings.EffectiveLearningExplanationGenerationProfile(modelName).MaximumOutputTokens).ToString(System.Globalization.CultureInfo.CurrentCulture);

            _installed.IsEnabled = !_busy;
            _template.IsEnabled = !_busy;
            _contextTokens.IsEnabled = !_busy;
            _outputTokens.IsEnabled = !_busy && !string.IsNullOrEmpty(modelName);
            _cancelTransfer.IsEnabled = _busy;
            UpdateCatalogActions();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateCatalogActions()
    {
        var installed = _models.Select(model => model.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var border in _catalog.Children.OfType<Border>())
        {
            if (border.Child is not Grid { Children.Count: 2 } grid || grid.Children[1] is not Button button) continue;
            var index = _catalog.Children.IndexOf(border);
            if (index < 0 || index >= Catalog.Count) continue;
            var model = Catalog[index];
            bool isInstalled = installed.Contains(model.Filename);
            button.IsEnabled = !_busy && !isInstalled;
            button.Content = isInstalled ? "Installed" : "Get";
        }
    }

    private void OnInstalledSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || _installed.SelectedValue is not string filename
            || string.Equals(filename, SelectedModel, StringComparison.Ordinal)) return;
        _context.Change(settings =>
        {
            if (_role == LearningModelRole.Translation) settings.LearningLocalModel = filename;
            else settings.LearningExplanationLocalModel = filename;
        });
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || _template.SelectedValue is not LearningChatTemplate template
            || template == SelectedTemplate) return;
        _context.Change(settings =>
        {
            if (_role == LearningModelRole.Translation) settings.LearningLocalTemplate = template;
            else settings.LearningExplanationLocalTemplate = template;
        });
    }

    private void CommitContextTokens(string text)
    {
        if (_refreshing || !int.TryParse(text, out int tokens))
        {
            UpdateControls();
            return;
        }

        var filename = _contextModelBeingEdited;
        if (string.IsNullOrEmpty(filename))
        {
            UpdateControls();
            return;
        }

        tokens = Math.Clamp(tokens, 1_024, ModelContextBudget.MaximumLocalTokens);
        _context.Change(settings =>
        {
            if (_role == LearningModelRole.Translation)
            {
                settings.LearningLocalContextTokensByModel ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                settings.LearningLocalContextTokensByModel[filename] = tokens;
            }
            else
            {
                settings.LearningExplanationLocalContextTokensByModel ??= new(StringComparer.OrdinalIgnoreCase);
                settings.LearningExplanationLocalContextTokensByModel[filename] = tokens;
            }
        });
    }

    private void CommitOutputTokens(string text)
    {
        if (_refreshing || string.IsNullOrEmpty(_outputModelBeingEdited) || !int.TryParse(text, out int tokens))
        { UpdateControls(); return; }
        var filename = _outputModelBeingEdited;
        var profile = _context.Settings.EffectiveLearningExplanationGenerationProfile(filename);
        _context.Change(settings =>
        {
            settings.LearningExplanationLocalGenerationProfiles ??= new(StringComparer.OrdinalIgnoreCase);
            settings.LearningExplanationLocalGenerationProfiles[filename] = profile with
            { MaximumOutputTokens = LearningExplanationModelPolicy.ResolveOutputTokens(tokens) };
        });
    }

    private async void RefreshInstalledAsync()
    {
        if (_lifetime.IsCancellationRequested) return;
        int generation = Interlocked.Increment(ref _refreshGeneration);
        try
        {
            var models = await _context.Host.LearningModels.GetInstalledModelsAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || generation != Volatile.Read(ref _refreshGeneration)) return;
            _models = models.ToArray();
            UpdateControls();
            _installedStatus.Text = $"{_models.Length} installed GGUF model(s). Weights load only on request.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested)
                _installedStatus.Text = "The installed learning models could not be listed.";
        }
    }

    private async Task DownloadModelAsync(LearningDownloadableModel model, TextBlock status)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        using var transfer = new CancellationTokenSource();
        _activeTransfer = transfer;
        _busy = true;
        UpdateControls();
        var progress = new Progress<LearningModelTransferProgress>(update =>
        {
            if (_lifetime.IsCancellationRequested || !ReferenceEquals(_activeTransfer, transfer)) return;
            status.Text = update.Completed
                ? "Installed and checksum verified."
                : update.TotalBytes is > 0
                    ? $"Downloading · {update.BytesDownloaded * 100 / update.TotalBytes.Value}%"
                    : $"Downloading · {update.BytesDownloaded / 1_000_000:N0} MB";
        });

        try
        {
            await _context.Host.LearningModels.DownloadAsync(model, progress, transfer.Token);
            if (_lifetime.IsCancellationRequested) return;
            status.Text = "Installed and checksum verified.";
            RefreshInstalledAsync();
        }
        catch (OperationCanceledException) when (transfer.IsCancellationRequested)
        {
            if (!_lifetime.IsCancellationRequested) status.Text = "Download cancelled.";
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested)
                status.Text = "The model could not be downloaded or verified. Retry after checking the connection and free disk space.";
        }
        finally
        {
            if (ReferenceEquals(_activeTransfer, transfer)) _activeTransfer = null;
            _busy = false;
            if (!_lifetime.IsCancellationRequested) UpdateControls();
        }
    }

    private void ImportModels()
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        var dialog = new OpenFileDialog
        {
            Title = "Add Learning GGUF Models",
            Filter = "GGUF models (*.gguf)|*.gguf",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        _ = ImportModelsAsync(dialog.FileNames);
    }

    private async Task ImportModelsAsync(string[] files)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        using var transfer = new CancellationTokenSource();
        _activeTransfer = transfer;
        _busy = true;
        _installedStatus.Text = "Importing selected GGUF files…";
        UpdateControls();
        try
        {
            var result = await _context.Host.LearningModels.ImportAsync(files, transfer.Token);
            if (_lifetime.IsCancellationRequested) return;
            _installedStatus.Text = $"Imported {result.Imported}; skipped {result.Skipped}; failed {result.Failed}.";
            RefreshInstalledAsync();
        }
        catch (OperationCanceledException) when (transfer.IsCancellationRequested)
        {
            if (!_lifetime.IsCancellationRequested) _installedStatus.Text = "Import cancelled.";
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested) _installedStatus.Text = "The selected model files could not be imported.";
        }
        finally
        {
            if (ReferenceEquals(_activeTransfer, transfer)) _activeTransfer = null;
            _busy = false;
            if (!_lifetime.IsCancellationRequested) UpdateControls();
        }
    }

    private async void OpenModelsFolder()
    {
        try
        {
            var directory = _context.Host.LearningModels.ModelsDirectory;
            var token = _lifetime.Token;
            await Task.Run(() => Directory.CreateDirectory(directory), token);
            if (!_lifetime.IsCancellationRequested) Ui.OpenPath(directory);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested)
                _installedStatus.Text = "Could not open the models folder.";
        }
    }

    private void CancelTransfers()
    {
        CancelActiveTransfer();
        try { _lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void CancelActiveTransfer()
    {
        try { _activeTransfer?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private ModelChoice Choice(string value, string label) => new(value, label, _role != LearningModelRole.Explanation
        || LearningExplanationModelPolicy.SupportsGeneralInstructions(value, LearningChatTemplate.Automatic));

    private sealed record ModelChoice(string Value, string Label, bool IsSelectable)
    {
        public override string ToString() => Label;
    }

    private sealed record TemplateChoice(LearningChatTemplate Value, string Label);
}
