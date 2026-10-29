using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>The batch selection is window-local and survives filters and leaving select mode.
/// It never alters the notebook's single detail selection or Store graph.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private bool _selectingReviewSenses;
    private readonly HashSet<VocabularySelection> _reviewSelections = [];
    private readonly Button _selectReview = new() { Name = "canonicalSelectSenses", Content = "Select senses", Padding = new Thickness(10, 5, 10, 5) };
    private readonly StackPanel _reviewSelectionBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 6, 18, 12) };
    private readonly TextBlock _reviewSelectionCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _selectVisible = new() { Name = "canonicalSelectVisible", Content = "Select visible" }, _clearReview = new() { Name = "canonicalClearSelection", Content = "Clear" },
        _prepareSelected = new() { Name = "canonicalPrepareSelected", Content = "Prepare selected" }, _finishSelection = new() { Name = "canonicalFinishSelection", Content = "Done" };
    private void InitializeReviewSelection(Panel toolbar, DockPanel root)
    {
        VocabularyNotebookTheme.Button(_selectReview); _selectReview.Margin = new Thickness(0, 0, 8, 8); toolbar.Children.Add(_selectReview);
        foreach (var button in new[] { _selectVisible, _clearReview, _prepareSelected, _finishSelection }) { VocabularyNotebookTheme.Button(button, button == _prepareSelected); button.Margin = new Thickness(0, 0, 8, 0); }
        _reviewSelectionBar.Children.Add(_reviewSelectionCount); _reviewSelectionBar.Children.Add(_selectVisible); _reviewSelectionBar.Children.Add(_clearReview); _reviewSelectionBar.Children.Add(_prepareSelected); _reviewSelectionBar.Children.Add(_finishSelection);
        DockPanel.SetDock(_reviewSelectionBar, Dock.Bottom); int contentIndex = root.Children.IndexOf(_content); root.Children.Insert(contentIndex < 0 ? root.Children.Count : contentIndex, _reviewSelectionBar);
        _selectReview.Click += (_, _) => { _selectingReviewSenses = !_selectingReviewSenses; if (_selectingReviewSenses && _session.MeaningMode) _organization.SelectedIndex = 0; _session.Refresh(); };
        _finishSelection.Click += (_, _) => { _selectingReviewSenses = false; _session.Refresh(); };
        _selectVisible.Click += (_, _) => { foreach (var entry in _session.Visible) foreach (var sense in entry.Senses) _reviewSelections.Add(new(entry.Id, sense.Id)); _session.Refresh(); };
        _clearReview.Click += (_, _) => { _reviewSelections.Clear(); _session.Refresh(); };
        _prepareSelected.Click += (_, _) => OpenReviewBatch(_reviewSelections.OrderBy(value => value.SenseId.ToString("D").ToUpperInvariant(), StringComparer.Ordinal).ToArray());
        UpdateReviewSelection();
    }
    private void UpdateReviewSelection()
    {
        _reviewSelectionBar.Visibility = _selectingReviewSenses ? Visibility.Visible : Visibility.Collapsed;
        _reviewSelectionCount.Text = $"{_reviewSelections.Count} selected"; _clearReview.IsEnabled = _reviewSelections.Count > 0;
        _prepareSelected.IsEnabled = _reviewSelections.Count > 0 && _reviewFeature is not null && !_retiring && _modalDone is null;
        _selectVisible.IsEnabled = _session.Visible.Count > 0 && !_retiring; _selectReview.IsEnabled = !_retiring && _reviewFeature is not null;
        _selectReview.Content = _selectingReviewSenses ? "Selecting senses" : "Select senses";
    }
    private object ReviewSenseHeader(F.VocabularyEntry entry, F.VocabularySense sense, FrameworkElement label)
    {
        if (!_selectingReviewSenses) return label;
        var selection = new VocabularySelection(entry.Id, sense.Id); var row = new StackPanel { Orientation = Orientation.Horizontal };
        var check = new CheckBox { IsChecked = _reviewSelections.Contains(selection), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(check, entry.Expression + " · " + SenseTitle(sense));
        check.Click += (_, e) => { if (check.IsChecked == true) _reviewSelections.Add(selection); else _reviewSelections.Remove(selection); UpdateReviewSelection(); e.Handled = true; };
        row.Children.Add(check); row.Children.Add(label); return row;
    }
}
