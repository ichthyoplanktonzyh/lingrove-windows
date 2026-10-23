using System.Windows.Controls;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Presentation ownership for notebook explanations. The window owns the injected session once;
/// stable usage keys retain row controls across acknowledged Store updates, preserving expanded
/// Details and text selection. Entry/sense navigation retires row subscriptions before replacement.
/// Durable adoption tasks join the window's existing work drain, independently of generation.
/// </summary>
public partial class VocabularyNotebookWindow
{
    private readonly Dictionary<string, VocabularyExplanationRow> _explanationRows = [];
    private VocabularyDictionarySection? _dictionarySection;
    private VocabularyNotebookSession? _learning;
    private Action? _configureExplanation;
    private bool _learningRetired;

    private void SelectLearning(VocabularyEntry? entry, VocabularySense? sense)
    {
        if (_learning is null || _learningRetired) return;
        bool ownerChanged = _learning.EntryId != entry?.Id || _learning.SenseId != sense?.Id;
        if (ownerChanged) ClearLearningRows();
        _learning.Select(entry, sense?.Id);
        foreach (var pair in _explanationRows.ToArray())
            if (!_learning.IsCurrent(pair.Key, pair.Value.Session)) { pair.Value.Dispose(); _explanationRows.Remove(pair.Key); }
    }
    private VocabularyExplanationRow? ExplanationRow(VocabularyEntry entry, VocabularySense sense, VocabularyExample? example)
    {
        if (_learning is null || !_learning.Explanations.TryGetValue(VocabularyNotebookTargetPolicy.Key(example), out var session)) return null;
        string key = VocabularyNotebookTargetPolicy.Key(example);
        if (!_explanationRows.TryGetValue(key, out var row))
        {
            row = new VocabularyExplanationRow(_store, _learning, key, session,
                () => !_retired && IsVisible && Selection is { } selected && selected.EntryId == entry.Id && selected.SenseId == sense.Id,
                Track, _configureExplanation);
            _explanationRows[key] = row;
        }
        // Rebuilding the surrounding detail attaches the same row to a new section container.
        // WPF keeps its old parent even after the outer subtree was removed from the window.
        if (row.Parent is Panel parent) parent.Children.Remove(row);
        row.Update(entry, sense, example); return row;
    }
    private void ClearLearningRows()
    {
        _dictionarySection?.Dispose(); _dictionarySection = null;
        foreach (var row in _explanationRows.Values) row.Dispose(); _explanationRows.Clear();
    }
    private void AddDictionarySection(VocabularyEntry entry, VocabularySense sense)
    {
        if (_learning is null || _learningRetired) return;
        _dictionarySection ??= new VocabularyDictionarySection(_learning, entry,
            () => !_retired && IsVisible && Selection is { } selected && selected.EntryId == entry.Id && selected.SenseId == sense.Id);
        if (_dictionarySection.Parent is Panel parent) parent.Children.Remove(_dictionarySection);
        _dictionarySection.Update(entry); DetailContent.Children.Add(_dictionarySection);
    }
    private void RetireLearning()
    {
        if (_learningRetired) return; _learningRetired = true; ClearLearningRows();
        if (_learning is not null) Track(_learning.DisposeAsync().AsTask());
    }
}
