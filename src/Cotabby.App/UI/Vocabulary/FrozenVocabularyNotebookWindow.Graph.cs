using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Window-local graph borrows the canonical snapshot and settings; it owns no Store.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private IVocabularyGraphPreferences? _graphPreferences;
    private VocabularyGraphPresentation? _graphPresentation;
    private VocabularyGraphView? _graphView;
    private readonly Button _graphButton = new() { Name = "canonicalGraphMode", Content = InterfaceText.Get("Graph"), Padding = new Thickness(10, 5, 10, 5), IsEnabled = false, ToolTip = InterfaceText.Get("Graph settings are unavailable", "Vocabulary") };
    private bool _graphMode;
    private double _graphDetailWidth = 360;
    private VocabularySelection? _lastGraphSelection;
    private VocabularyMeaningDestination? _lastGraphDestination;
    internal VocabularyGraphView? CanonicalGraphView => _graphView;
    internal void AttachGraphPreferences(IVocabularyGraphPreferences preferences)
    {
        Dispatcher.VerifyAccess(); if (_retiring) return; if (_graphPreferences is not null && !ReferenceEquals(_graphPreferences, preferences)) throw new InvalidOperationException("Graph preferences are already attached."); if (_graphPreferences is not null) return;
        _graphPreferences = preferences; _graphPresentation = new(preferences); _graphView = new(_graphPresentation, new WindowsVocabularyTextSearch(), task => _ = Track(task)); _graphMode = preferences.BrowserMode == "graph"; _graphButton.IsEnabled = true; _graphButton.ToolTip = InterfaceText.Get("Switch between list and graph", "Vocabulary"); _session.Refresh();
    }
    private void InitializeGraphButton() { VocabularyNotebookTheme.Button(_graphButton); _graphButton.Click += (_, _) => SetGraphMode(!_graphMode); }
    private void SetGraphMode(bool graph)
    {
        if (_retiring || _modalDone is not null || _graphPreferences is null || _graphPresentation is null || _graphMode == graph) return; _graphMode = graph; _graphPreferences.BrowserMode = graph ? "graph" : "list"; if (graph) { _selectingReviewSenses = false; _graphPresentation.Reveal(_session.Selection); _lastGraphSelection = _session.Selection; _lastGraphDestination = null; } _session.Refresh();
    }
    private void ReconcileGraphFilter()
    {
        if (_session.MeaningMode) { if (_session.Destination?.NodeId is { } node && !_session.MeaningItems.Any(item => item.Id == node)) { _session.Destination = null; _graphPresentation?.ClearFocus(); } return; }
        if (!_graphMode || _session.Selection is not { } selected) return;
        if (!_session.Visible.Any(e => e.Id == selected.EntryId && e.Senses.Any(s => s.Id == selected.SenseId))) { _session.Selection = null; if (_session.Destination?.Kind == VocabularyMeaningDestinationKind.Sense) _session.Destination = _session.Destination.NodeId is { } node ? new(VocabularyMeaningDestinationKind.Node, node) : null; _graphPresentation?.ClearFocus(); }
    }
    private void BuildGraphLayout()
    {
        if (_graphView is null || _graphPresentation is null || _session.Snapshot is not { } snapshot) return; var doc = snapshot.Document; var entries = _session.Visible.ToArray(); var query = _session.Query; var meaning = _session.MeaningMode; var languages = _session.PreferredLanguages.ToArray(); var includeEmpty = _session.Language is null && _session.Tag.Length == 0; var unexplored = _outline.ShowUnexplored; var search = new WindowsVocabularyTextSearch();
        if (!meaning && _lastGraphSelection != _session.Selection) { _lastGraphSelection = _session.Selection; _graphPresentation.Reveal(_session.Selection); }
        if (meaning && _lastGraphDestination != _session.Destination) { _lastGraphDestination = _session.Destination; if (_session.Destination?.NodeId is { } node) { var categories = doc.Classification.Memberships.Where(m => m.NodeId == node && m.Provenance.Status == Cotabby.Core.Vocabulary.Resources.VocabularySemanticStatus.Confirmed).Select(m => m.CategoryId).ToHashSet(); _graphPresentation.RevealMeaning(node, Ancestors(categories)); } else if (_session.Destination?.CategoryId is { } category) _graphPresentation.RevealCategory(category, Ancestors(new HashSet<Guid> { category })); }
        HashSet<Guid> Ancestors(IReadOnlySet<Guid> ids) { var found = ids.ToHashSet(); var edges = doc.Classification.Parents.ToLookup(x => x.CategoryId); var queue = new Queue<Guid>(ids); while (queue.TryDequeue(out var id)) foreach (var edge in edges[id]) if (found.Add(edge.ParentId)) queue.Enqueue(edge.ParentId); return found; }
        var graphSelection = meaning && _session.Destination?.Kind != VocabularyMeaningDestinationKind.Sense ? null : _session.Selection;
        _graphView.UpdateCanonical(expanded => meaning ? CanonicalVocabularyGraphProjection.Meanings(doc.Semantics, entries, expanded, query, search, StringComparer.CurrentCultureIgnoreCase, languages, [VocabularyCategoryUiLanguage.Identifier], SenseTitle, GraphLanguageLabel, doc.Classification, includeEmpty: includeEmpty, showsUnexplored: unexplored) : CanonicalVocabularyGraphProjection.Entries(entries, expanded, query, search, SenseTitle, GraphLanguageLabel), graphSelection, SelectFromCanonicalGraph, meaning ? _session.Destination?.NodeId : null, meaning ? _session.Destination?.CategoryId : null, id => { ++_actionSelectionEpoch; ShowMeaning(id); }, id => { ++_actionSelectionEpoch; _session.Destination = new(VocabularyMeaningDestinationKind.Category, CategoryId: id); _session.Refresh(); }, unexplored, value => { _outline.ShowUnexplored = value; if (_listPreferences is not null) _listPreferences.ShowUnexplored = value; _session.Refresh(); });
        bool detail = meaning ? _session.Destination is not null : _session.Selection is not null; _content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 350 }); if (detail) { _content.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); _content.ColumnDefinitions.Add(new() { Width = new GridLength(_graphDetailWidth), MinWidth = 300, MaxWidth = 480 }); }
        Grid.SetColumn(_graphView, 0); _content.Children.Add(_graphView); if (detail) { var split = VocabularyNotebookTheme.Splitter(); split.DragCompleted += (_, _) => { if (_content.ColumnDefinitions.Count == 3) _graphDetailWidth = Math.Clamp(_content.ColumnDefinitions[2].ActualWidth, 300, 480); }; Grid.SetColumn(split, 1); _content.Children.Add(split); var shell = new DockPanel(); var close = new Button { Name = "canonicalGraphCloseDetail", Content = InterfaceText.Get("Close detail", "Vocabulary"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) }; VocabularyNotebookTheme.Button(close); close.Click += (_, _) => { _graphPresentation.ClearFocus(); SelectFromCanonicalGraph(null); }; DockPanel.SetDock(close, Dock.Top); shell.Children.Add(close); shell.Children.Add(Detail()); Grid.SetColumn(shell, 2); _content.Children.Add(shell); }
        _graphButton.Content = InterfaceText.Get("List");
    }
    private void SelectFromCanonicalGraph(VocabularySelection? selection)
    {
        if (_retiring || !_graphMode || !IsVisible || _graphView is not { IsEnabled: true, IsVisible: true }) return;
        if (selection is { } value) SelectSense(value.SenseId, _session.MeaningMode ? _session.Destination?.NodeId : null);
        else { ++_actionSelectionEpoch; _session.Selection = null; if (_session.MeaningMode) _session.Destination = null; _session.Refresh(); }
    }
    private static string GraphLanguageLabel(Cotabby.Core.LanguageLearning.LearningLanguage? language)
    {
        if (language is not { } value) return "Unspecified"; try { return System.Globalization.CultureInfo.GetCultureInfo(Cotabby.Core.LanguageLearning.LearningLanguages.Code(value)).NativeName; } catch (System.Globalization.CultureNotFoundException) { return Cotabby.Core.LanguageLearning.LearningLanguages.Name(value); }
    }
}
