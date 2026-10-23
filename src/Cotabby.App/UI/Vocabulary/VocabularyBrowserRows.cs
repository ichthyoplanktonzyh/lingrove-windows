using System.ComponentModel;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Literal row values borrow acknowledged entries; WPF virtualizes the visual containers.
/// Expansion and transient batch selection belong to the window, never to durable notebook edits.
/// </summary>
public sealed class VocabularyEntryRow
{
    private readonly Action<bool> _expanded;
    private readonly VocabularyEntry _entry;
    private bool _isExpanded;
    internal VocabularyEntryRow(VocabularyEntry entry, bool expanded, Guid? selected, Action<bool> setExpanded,
        bool selecting = false, IReadOnlySet<VocabularySelection>? batch = null, Action<VocabularySelection, bool>? selectBatch = null, long offerVersion = 0)
    {
        _entry = entry; _isExpanded = expanded; _expanded = setExpanded;
        Senses = entry.Senses.Select(sense => new VocabularySenseRow(entry.Id, sense, sense.Id == selected,
            selecting, batch?.Contains(new(entry.Id, sense.Id)) == true, selectBatch, offerVersion, entry.Expression)).ToArray();
    }
    public string Expression => _entry.Expression;
    public IReadOnlyList<VocabularySenseRow> Senses { get; }
    public bool IsSelected => false;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; _expanded(value); } }
}
/// <summary>Sense identity and read-only label/encounter preview; selecting a label never changes that identity.</summary>
public sealed class VocabularySenseRow(Guid entryId, VocabularySense sense, bool selected = false,
    bool selecting = false, bool batchSelected = false, Action<VocabularySelection, bool>? selectBatch = null,
    long offerVersion = 0, string expression = "") : INotifyPropertyChanged
{
    private bool _batchSelected = batchSelected, _batchEnabled = true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public VocabularySelection Selection { get; } = new(entryId, sense.Id);
    public string Title { get; } = VocabularyNotebookPolicy.DisplayTitle(sense);
    public string UsageCount { get; } = $"{sense.Examples.Count} usages";
    public string Example { get; } = sense.Examples.FirstOrDefault()?.Text ?? "";
    // Both row types expose the container's two presentation bindings. A leaf never expands;
    // selection is initialized from the window's stable sense ID after each snapshot refresh.
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; } = selected;
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
}
