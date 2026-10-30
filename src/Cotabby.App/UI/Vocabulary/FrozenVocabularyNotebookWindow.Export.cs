using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>The notebook freezes offered IDs; the borrowed Store resolves them on its own current snapshot.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private VocabularyExportWindow? _canonicalExport;
    private readonly Button _canonicalExportButton = new() { Name = "canonicalExport", Content = InterfaceText.Get("Export…"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
    private void InitializeCanonicalExport(Panel toolbar)
    {
        VocabularyNotebookTheme.Button(_canonicalExportButton); toolbar.Children.Add(_canonicalExportButton);
        _canonicalExportButton.Click += (_, _) => LastOperation = OpenCanonicalExportAsync();
    }
    private void UpdateCanonicalExport() => _canonicalExportButton.IsEnabled = !_exampleNoExternalFiles && !_retiring && _modalDone is null && _session.Snapshot is not null && _session.Failure is null && _store is ICanonicalVocabularyExportStore && _session.Entries.Count > 0;
    private async Task OpenCanonicalExportAsync(VocabularySelection? single = null)
    {
        if (_exampleNoExternalFiles || _retiring || _modalDone is not null || _store is not ICanonicalVocabularyExportStore store || _session.Snapshot is null) return;
        static VocabularySelection[] Select(IEnumerable<Cotabby.Core.Vocabulary.Frozen.VocabularyEntry> entries) => entries.SelectMany(entry => entry.Senses.Select(sense => new VocabularySelection(entry.Id, sense.Id))).ToArray();
        var ordered = _session.NewestFirst ? _session.Entries.OrderByDescending(e => e.UpdatedAt.ReferenceSeconds) : _session.Entries.OrderBy(e => e.UpdatedAt.ReferenceSeconds);
        var all = Select(ordered); var filtered = _session.MeaningMode ? _session.MeaningItems.SelectMany(item => item.Selections).Concat(_session.Unclassified).Distinct().ToArray() : Select(_session.Visible); var selected = single is { } value ? [value] : all.Where(_reviewSelections.Contains).ToArray();
        var initial = single is not null || selected.Length > 0 ? VocabularyExportScope.Selected : _session.Query.Length == 0 && _session.Language is null && _session.Tag.Length == 0 ? VocabularyExportScope.All : VocabularyExportScope.Filtered;
        var request = new VocabularyExportRequest(all, filtered, selected, initial);
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var window = _canonicalExport = new VocabularyExportWindow(store, request) { Owner = this };
            try { window.ShowDialog(); } finally { await window.RetireAsync(); _canonicalExport = null; }
        }
        finally
        {
            _canonicalExport = null; _modalDone = null;
            try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); }
            finally { done.TrySetResult(); }
        }
    }
}
