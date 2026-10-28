using System.ComponentModel;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Literal row values borrow acknowledged entries; WPF virtualizes the visual containers.
/// Expansion and transient batch selection belong to the window, never to durable notebook edits.
/// Senses retains every projected identity for batch/export; Children exposes only multi-sense
/// disclosure rows, so singleton selection needs no expansion. The window updates selection flags
/// in place, allowing compact inline meaning to change without replacing or resizing a container.
/// </summary>
public sealed class VocabularyEntryRow : INotifyPropertyChanged
{
    private readonly Action<bool> _expanded;
    private readonly VocabularyEntry _entry;
    private bool _isExpanded;
    private bool _selected;
    private readonly bool _detailed;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal VocabularyEntryRow(VocabularyEntry entry, bool expanded, Guid? selected, Action<bool> setExpanded,
        bool selecting = false, IReadOnlySet<VocabularySelection>? batch = null, Action<VocabularySelection, bool>? selectBatch = null, long offerVersion = 0,
        bool detailed = false, IReadOnlyDictionary<Guid, string>? snippets = null)
    {
        _entry = entry; _isExpanded = expanded; _expanded = setExpanded; _detailed = detailed;
        OfferVersion = offerVersion;
        Senses = entry.Senses.Select((sense, index) => new VocabularySenseRow(entry.Id, sense, sense.Id == selected,
            selecting, batch?.Contains(new(entry.Id, sense.Id)) == true, selectBatch, offerVersion, entry.Expression,
            index + 1, snippets?.GetValueOrDefault(sense.Id) ?? "")).ToArray();
        _selected = Senses.Any(sense => sense.IsSelected);
    }
    public string Expression => _entry.Expression;
    public IReadOnlyList<VocabularySenseRow> Senses { get; }
    public Guid EntryId => _entry.Id;
    internal long OfferVersion { get; }
    public string LanguageLabel => _entry.Language.Label();
    public bool IsSingle => Senses.Count == 1;
    public VocabularySenseRow? SingleSense => IsSingle ? Senses[0] : null;
    public bool ShowBatchCheckbox => IsSingle && SingleSense!.Selecting;
    public IReadOnlyList<VocabularySenseRow> Children => IsSingle ? [] : Senses;
    public string Summary => string.Join(" · ", Senses.Select(sense => sense.Title).Where(title => title.Length > 0));
    public string SenseCount => IsSingle ? "" : Senses.Count.ToString();
    public bool IsEntrySelected => _selected;
    public bool IsSelected => IsSingle && _selected;
    public bool ShowInlineMeaning => IsSingle && !_detailed && _selected && SingleSense!.Title.Length > 0;
    public bool ShowDetailedMeaning => _detailed && (IsSingle || Summary.Length > 0);
    public string DetailedMeaning => IsSingle ? SingleSense!.Title : Summary;
    public string Snippet => SingleSense?.Snippet ?? "";
    public bool HasSnippet => Snippet.Length > 0;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value; _expanded(value); PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
        }
    }
    internal void PresentSelection(Guid? selected)
    {
        foreach (var sense in Senses) sense.PresentSelection(sense.Selection.SenseId == selected);
        bool next = Senses.Any(sense => sense.IsSelected); if (next == _selected) return; _selected = next;
        foreach (string property in new[] { nameof(IsSelected), nameof(IsEntrySelected), nameof(ShowInlineMeaning) })
            PropertyChanged?.Invoke(this, new(property));
    }
}
/// <summary>Sense identity and read-only label/encounter preview; selecting a label never changes that identity.</summary>
public sealed class VocabularySenseRow(Guid entryId, VocabularySense sense, bool selected = false,
    bool selecting = false, bool batchSelected = false, Action<VocabularySelection, bool>? selectBatch = null,
    long offerVersion = 0, string expression = "", int index = 1, string snippet = "") : INotifyPropertyChanged
{
    private bool _batchSelected = batchSelected, _batchEnabled = true;
    private bool _selected = selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public VocabularySelection Selection { get; } = new(entryId, sense.Id);
    public string Title { get; } = VocabularyNotebookPolicy.DisplayTitle(sense);
    public string UsageCount { get; } = $"{sense.Examples.Count} usages";
    public string Example { get; } = sense.Examples.FirstOrDefault()?.Text ?? "";
    // Both row types expose the container's two presentation bindings. A leaf never expands;
    // selection is initialized from the window's stable sense ID after each snapshot refresh.
    public bool IsExpanded { get; set; }
    public bool IsSelected => _selected;
    public string Number => index.ToString();
    public string UsageNumber => sense.Examples.Count == 0 ? "" : sense.Examples.Count.ToString();
    public string Snippet => snippet;
    public bool HasSnippet => Snippet.Length > 0;
    public bool Selecting => selecting;
    public string BatchLabel => expression + " · " + Title;
    internal long OfferVersion => offerVersion;
    public bool BatchEnabled
    { get => _batchEnabled; internal set { _batchEnabled = value; PropertyChanged?.Invoke(this, new(nameof(BatchEnabled))); } }
    public bool IsBatchSelected
    {
        get => _batchSelected;
        set
        {
            if (_batchSelected == value) return;
            _batchSelected = value; selectBatch?.Invoke(Selection, value);
            PropertyChanged?.Invoke(this, new(nameof(IsBatchSelected)));
        }
    }
    internal void PresentBatch(bool value)
    { _batchSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsBatchSelected))); }
    internal void PresentSelection(bool value)
    { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
}
