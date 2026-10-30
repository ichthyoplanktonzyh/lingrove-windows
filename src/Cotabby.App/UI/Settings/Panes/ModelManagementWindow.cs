using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Microsoft.Win32;
using System.IO;
using Cotabby.Win32.Models;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>Task-scoped presentation. The owner supplies snapshots and handles all file/runtime effects.</summary>
internal sealed class ModelManagementView : DockPanel
{
    private readonly StackPanel _installed = new();
    private readonly StackPanel _custom = new();
    private readonly Expander _customDisclosure;
    internal event Action<string>? SelectRequested;
    internal event Action<string>? AssignRequested;
    internal event Action<string>? DeleteRequested;
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    internal Panel InstalledRows => _installed;
    internal Panel CustomRows => _custom;
    internal Expander CustomDisclosure => _customDisclosure;
    internal Button Done { get; } = new() { Content = InterfaceText.Get("Done"), IsCancel = true, Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Center };
    internal Button? CancelTransfer { get; }

    internal ModelManagementView(string taskLabel, FrameworkElement recommendations, Action refresh, Action openFolder, Action? import, Action? cancelTransfer = null)
    {
        Background = SystemColors.WindowBrush;
        var header = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(Done, Dock.Right); header.Children.Add(Done);
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text = InterfaceText.Get("Manage models"), FontSize = 16, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = taskLabel, FontSize = 12, Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 4, 0, 0) } } });
        DockPanel.SetDock(header, Dock.Top); Children.Add(header);
        var divider = new Separator { Margin = new Thickness(0) }; DockPanel.SetDock(divider, Dock.Top); Children.Add(divider);
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Installed models", "Localizable"), FontSize = 13, FontWeight = FontWeights.SemiBold });
        body.Children.Add(Status); body.Children.Add(_installed);
        _customDisclosure = new Expander { Header = InterfaceText.Get("Custom models"), Content = _custom, Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(_customDisclosure);
        body.Children.Add(new TextBlock { Text = InterfaceText.Get("Recommended models", "Localizable"), FontSize = 12, Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 10, 0, 8) });
        body.Children.Add(recommendations);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (import is not null) actions.Children.Add(ActionButton("Add Your Own", import));
        if (cancelTransfer is not null) { CancelTransfer = ActionButton("Cancel transfer", cancelTransfer); CancelTransfer.IsEnabled = false; actions.Children.Add(CancelTransfer); }
        actions.Children.Add(ActionButton("Refresh", refresh));
        actions.Children.Add(ActionButton("Open Folder", openFolder));
        body.Children.Add(actions);
        if (import is not null) body.Children.Add(new TextBlock { Text = InterfaceText.Get("Imported models are used only for this task."), FontSize = 11, Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    internal void ShowModels(IReadOnlyList<LearningInstalledModel> models, AppSettings settings, LocalModelTask task, string? selected, string? directory = null, string? residentCompletion = null)
    {
        _installed.Children.Clear(); _custom.Children.Clear();
        var protectedNames = settings.ProtectedModelFilenames(residentCompletion);
        foreach (var model in models)
        {
            var filename = model.Filename;
            bool candidate = settings.IsLocalModelCandidate(filename, task);
            bool unassigned = LocalModelCatalog.Descriptor(filename) is null && LocalModelTaskPolicy.Supports(filename, task) && !candidate;
            string? source = null;
            settings.LocalModelSourcesValue?.TryGetValue(filename.ToLowerInvariant(), out source);
            if (candidate) _installed.Children.Add(ModelRow(model, string.Equals(filename, selected, StringComparison.OrdinalIgnoreCase),
                protectedNames.Contains(filename), () => SelectRequested?.Invoke(filename), directory: directory, source: source,
                delete: () => DeleteRequested?.Invoke(filename)));
            else if (unassigned) _custom.Children.Add(ModelRow(model, false, false, () => AssignRequested?.Invoke(filename), "Use for this task", directory, source));
        }
        if (!string.IsNullOrEmpty(selected) && !models.Any(model => string.Equals(model.Filename, selected, StringComparison.OrdinalIgnoreCase)
            && settings.IsLocalModelCandidate(model.Filename, task)))
            _installed.Children.Insert(0, new TextBlock { Text = ModelDisplayText.Unavailable(selected, task),
                Foreground = SystemColors.GrayTextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
        _customDisclosure.Visibility = _custom.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_installed.Children.Count == 0) _installed.Children.Add(new TextBlock { Text = InterfaceText.Get("No models available for this task.", "Localizable"), Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(0, 6, 0, 6) });
    }

    private static FrameworkElement ModelRow(LearningInstalledModel model, bool selected, bool inUse, Action action, string actionLabel = "Select", string? directory = null, string? source = null, Action? delete = null)
    {
        var title = ModelDisplayText.Name(model.Filename);
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        var detail = new Expander { Header = InterfaceText.Get("Model details"), FontSize = 11, Foreground = SystemColors.GrayTextBrush };
        var descriptor = LocalModelCatalog.Descriptor(model.Filename);
        var lines = new List<string> { ModelDisplayText.Safe(model.Filename) };
        if (descriptor is not null) lines.Add("Quantization: " + descriptor.Quantization);
        if (!string.IsNullOrEmpty(source)) lines.Add("Model source: " + ModelDisplayText.Safe(source));
        if (!string.IsNullOrEmpty(directory)) lines.Add(ModelDisplayText.Safe(System.IO.Path.Combine(directory, model.Filename)));
        detail.Content = new TextBlock { Text = string.Join("\n", lines), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };
        details.Children.Add(detail);
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        FrameworkElement trailing = selected ? new TextBlock { Text = "✓", FontSize = 16, Foreground = SystemColors.HighlightBrush, VerticalAlignment = VerticalAlignment.Center }
            : inUse ? new TextBlock { Text = InterfaceText.Get("In Use"), Foreground = SystemColors.GrayTextBrush, VerticalAlignment = VerticalAlignment.Center }
            : ActionButton(actionLabel, action);
        if (!selected && inUse && actionLabel == "Select") trailing = new StackPanel { Orientation = Orientation.Horizontal, Children = { trailing, ActionButton(actionLabel, action) } };
        if (!selected && !inUse && delete is not null)
        {
            var remove = ActionButton("Delete", delete);
            AutomationProperties.SetName(remove, "Delete " + ModelDisplayText.Safe(model.Filename));
            trailing = new StackPanel { Orientation = Orientation.Horizontal, Children = { trailing, remove } };
        }
        AutomationProperties.SetName(trailing, selected ? "Selected" : inUse ? "In Use" : actionLabel + " " + ModelDisplayText.Safe(model.Filename));
        DockPanel.SetDock(trailing, Dock.Right); row.Children.Add(trailing); row.Children.Add(details);
        return row;
    }
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
        button.Click += (_, _) => action(); return button;
    }
}

/// <summary>Window owns only listing lifetime. Existing app-owned manager performs scans/transfers.</summary>
internal sealed class ModelManagementWindow : Window
{
    private readonly SettingsContext _context;
    private readonly LocalModelTask _task;
    private readonly Action _parentRefresh;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ModelManagementView _view;
    private readonly ModelScanLifetime _scan = new();
    private string _notice = "";
    private int _operationVersion;
    private bool _importing;
    private CancellationTokenSource? _completionImport;

    internal ModelManagementWindow(SettingsContext context, LocalModelTask task, FrameworkElement recommendations, Action? import, Action parentRefresh, Action? cancelTransfer = null)
    {
        _context = context; _task = task; _parentRefresh = parentRefresh;
        Title = InterfaceText.Get("Manage models"); MinWidth = 620; Width = 720; MinHeight = 480; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _view = new ModelManagementView(task switch { LocalModelTask.Completion => "Autocomplete", LocalModelTask.Translation => "Translation", LocalModelTask.Explanation => "AI explanation", _ => "Speech" },
            recommendations, Refresh, OpenFolder, import ?? (task == LocalModelTask.Completion ? ImportCompletion : null),
            cancelTransfer ?? (task == LocalModelTask.Completion ? CancelCompletionImport : null));
        _view.Done.Click += (_, _) => Close();
        _view.SelectRequested += Select;
        _view.AssignRequested += Assign;
        _view.DeleteRequested += Delete;
        Content = _view;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => { _scan.Close(); _lifetime.Cancel(); _lifetime.Dispose(); _parentRefresh(); };
    }

    private string Selected() => _task switch
    {
        LocalModelTask.Completion => AppSettings.CompletionFilenameForId(_context.Settings.ActiveModelId) ?? "",
        LocalModelTask.Translation => _context.Settings.LearningLocalModel ?? "",
        LocalModelTask.Explanation => _context.Settings.LearningExplanationLocalModel ?? "",
        _ => "",
    };
    internal async void Refresh()
    {
        _view.Status.Text = InterfaceText.Get("Loading models…", "Localizable");
        await _scan.RunAsync(() => _context.Host.LearningModels.GetInstalledModelsAsync(_lifetime.Token), models =>
        {
            _context.Host.ReconcileCompletionSelection(models);
            _view.ShowModels(models, _context.Settings, _task, Selected(), _context.Host.LearningModels.ModelsDirectory, _context.Host.Runtime.ActiveModel?.FileName);
            _view.Status.Text = _notice;
        }, () => _view.Status.Text = InterfaceText.Get("Models could not be listed. Refresh to try again.", "Localizable"), _lifetime.Token);
    }
    internal void Report(string text) { _operationVersion++; SetNotice(text); }
    private void SetNotice(string text) { _notice = text; _view.Status.Text = text; }
    private void ReportCurrent(int version, string text) { if (version == _operationVersion && !_lifetime.IsCancellationRequested) SetNotice(text); }
    internal void SetTransferBusy(bool busy) { if (_view.CancelTransfer is { } cancel) cancel.IsEnabled = busy; }
    private void Select(string filename)
    {
        if (!_context.Settings.IsLocalModelCandidate(filename, _task)) return;
        _context.Change(settings =>
        {
            if (_task == LocalModelTask.Translation) settings.LearningLocalModel = filename;
            else if (_task == LocalModelTask.Explanation) settings.LearningExplanationLocalModel = filename;
            else if (_task == LocalModelTask.Completion)
                settings.ActiveModelId = ModelCatalog.All.FirstOrDefault(model => string.Equals(model.FileName, filename, StringComparison.OrdinalIgnoreCase))?.Id ?? "custom:" + filename;
        });
        Refresh(); _parentRefresh();
    }
    private void Assign(string filename)
    {
        if (!LocalModelTaskPolicy.Supports(filename, _task)) return;
        _context.Change(settings => settings.RecordLocalModelTask(filename, _task));
        Refresh(); _parentRefresh();
    }
    private async void ImportCompletion()
    {
        if (_importing) return;
        var dialog = new OpenFileDialog { Title = "Add GGUF Models", Filter = "GGUF models (*.gguf)|*.gguf", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var accepted = dialog.FileNames.Where(path => LocalModelTaskPolicy.Supports(Path.GetFileName(path), _task)).ToArray();
        if (accepted.Length == 0) { Report("This model is not compatible with this task. Choose another model."); return; }
        _context.Change(settings => { foreach (var path in accepted) settings.RecordLocalModelTask(Path.GetFileName(path), _task); });
        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _completionImport = transfer; _importing = true; SetTransferBusy(true); int version = ++_operationVersion; SetNotice("Importing selected GGUF files…");
        try
        {
            var result = await _context.Host.LearningModels.ImportAsync(accepted, transfer.Token,
                filename => Dispatcher.InvokeAsync(() => _context.Host.AcknowledgeModelMetadata(settings => settings.ClearLocalModelSource(filename))).Task);
            if (!_lifetime.IsCancellationRequested) { ReportCurrent(version, $"{(result.Cancelled ? "Import cancelled. " : "")}Imported {result.Imported}; skipped {result.Skipped}; failed {result.Failed}; incompatible {dialog.FileNames.Length - accepted.Length}."); Refresh(); _parentRefresh(); }
        }
        catch (OperationCanceledException) { ReportCurrent(version, "Import cancelled."); }
        catch (Exception) { ReportCurrent(version, "The selected model files could not be imported."); }
        finally { _completionImport = null; _importing = false; if (!_lifetime.IsCancellationRequested) SetTransferBusy(false); }
    }
    private void CancelCompletionImport() => _completionImport?.Cancel();

    private async void Delete(string filename)
    {
        if (MessageBox.Show(this, $"Remove {ModelDisplayText.Name(filename)} from the local models folder?", "Delete model?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int version = ++_operationVersion; SetNotice("Deleting model…");
        try
        {
            bool deleted = await _context.Host.LearningModels.DeleteAsync(filename, path => Dispatcher.InvokeAsync(() =>
            {
                if (_lifetime.IsCancellationRequested || _context.Settings.ProtectedModelFilenames(_context.Host.Runtime.ActiveModel?.FileName).Contains(filename)) return false;
                if (!File.Exists(path)) return false;
                WindowsModelFile.Delete(_context.Host.LearningModels.ModelsDirectory, filename);
                _context.Host.AcknowledgeModelMetadata(settings => settings.ClearLocalModelSource(filename));
                return true;
            }).Task, _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) { ReportCurrent(version, deleted ? "Model deleted." : "This model is unavailable or in use."); Refresh(); _parentRefresh(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { ReportCurrent(version, "The model could not be deleted."); }
    }
    private async void OpenFolder()
    {
        try
        {
            var directory = _context.Host.LearningModels.ModelsDirectory;
            await System.Threading.Tasks.Task.Run(() => System.IO.Directory.CreateDirectory(directory), _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) Ui.OpenPath(directory);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _view.Status.Text = InterfaceText.Get("Could not open the models folder.", "Localizable"); }
    }
}
