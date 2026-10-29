using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Window-owned learning sessions borrow the canonical Store and app's single providers.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private CanonicalNotebookLearningSession? _learning;
    private readonly Dictionary<string, CanonicalVocabularyExplanationRow> _learningRows = [];
    private CanonicalVocabularyDictionarySection? _learningDictionary;
    private Action? _configureLearning;
    internal void AttachLearning(ILearningExplanationGenerating service, ILearningDictionaryProviding dictionary,
        ILearningLexicalText lexical, Func<string, IReadOnlyList<LearningDictionaryWord>> unknownWords, Action configure)
    {
        Dispatcher.VerifyAccess();
        if (_learning is not null) throw new InvalidOperationException("Learning is already attached.");
        _learning = new(Dispatcher, dictionary, lexical, service, unknownWords); _configureLearning = configure;
        if (_started && !_retiring) _session.Refresh();
    }
    private void SelectLearning(F.VocabularyEntry? entry, F.VocabularySense? sense)
    {
        if (_learning is null) return;
        if (_learning.EntryId != entry?.Id || _learning.SenseId != sense?.Id) ClearLearningRows();
        _learning.Select(entry, sense?.Id);
        foreach (var pair in _learningRows.ToArray())
            if (!_learning.IsCurrent(pair.Key, pair.Value.Session)) { pair.Value.Dispose(); _learningRows.Remove(pair.Key); }
    }
    private void AddLearningRow(StackPanel panel, F.VocabularyEntry entry, F.VocabularySense sense, F.VocabularyExample? example)
    {
        if (_learning is null || _store is not ICanonicalVocabularyStore store) return;
        string key = CanonicalNotebookTargetPolicy.Key(example);
        if (!_learning.Explanations.TryGetValue(key, out var session)) return;
        if (!_learningRows.TryGetValue(key, out var row))
        {
            row = new(store, _learning, key, session, () => !_retiring && IsVisible && _session.Selection is { } selected
                && selected.EntryId == entry.Id && selected.SenseId == sense.Id, operation => _ = Track(operation), _configureLearning);
            _learningRows[key] = row;
        }
        if (row.Parent is Panel parent) parent.Children.Remove(row);
        row.Update(entry, sense, example); panel.Children.Add(row);
    }
    private void AddLearningDictionary(StackPanel panel, F.VocabularyEntry entry, F.VocabularySense sense)
    {
        if (_learning is null) return;
        _learningDictionary ??= new(_learning, entry, () => !_retiring && IsVisible && _session.Selection is { } selected
            && selected.EntryId == entry.Id && selected.SenseId == sense.Id);
        if (_learningDictionary.Parent is Panel parent) parent.Children.Remove(_learningDictionary);
        _learningDictionary.Update(entry); panel.Children.Add(_learningDictionary);
    }
    private void ClearLearningRows()
    {
        _learningDictionary?.Dispose(); _learningDictionary = null;
        foreach (var row in _learningRows.Values) row.Dispose(); _learningRows.Clear();
    }
    private void CancelLearning() => _learning?.Cancel();
    private async Task RetireLearningAsync()
    {
        ClearLearningRows(); if (_learning is { } learning) { _learning = null; await learning.DisposeAsync(); }
    }
}
