using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Immutable row values borrow acknowledged entries; WPF virtualizes the visual containers.
/// Expansion is the one window-owned browsing preference, independent of durable notebook edits.
/// </summary>
public sealed class VocabularyEntryRow
{
    private readonly Action<bool> _expanded;
    private readonly VocabularyEntry _entry;
    private bool _isExpanded;
    internal VocabularyEntryRow(VocabularyEntry entry, bool expanded, Guid? selected, Action<bool> setExpanded)
    {
        _entry = entry; _isExpanded = expanded; _expanded = setExpanded;
        Senses = entry.Senses.Select(sense => new VocabularySenseRow(entry.Id, sense, sense.Id == selected)).ToArray();
    }
    public string Expression => _entry.Expression;
    public IReadOnlyList<VocabularySenseRow> Senses { get; }
    public bool IsSelected => false;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; _expanded(value); } }
}
/// <summary>Sense identity and read-only label/encounter preview; selecting a label never changes that identity.</summary>
public sealed class VocabularySenseRow(Guid entryId, VocabularySense sense, bool selected = false)
{
    public VocabularySelection Selection { get; } = new(entryId, sense.Id);
    public string Title { get; } = VocabularyNotebookPolicy.DisplayTitle(sense);
    public string UsageCount { get; } = $"{sense.Examples.Count} usages";
    public string Example { get; } = sense.Examples.FirstOrDefault()?.Text ?? "";
    // Both row types expose the container's two presentation bindings. A leaf never expands;
    // selection is initialized from the window's stable sense ID after each snapshot refresh.
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; } = selected;
}
