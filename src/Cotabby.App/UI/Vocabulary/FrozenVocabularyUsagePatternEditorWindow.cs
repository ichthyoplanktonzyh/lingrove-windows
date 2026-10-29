using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Detached practical wording draft; structured slots and provenance stay untouched.</summary>
internal sealed class FrozenVocabularyUsagePatternEditorWindow : FrozenVocabularySemanticFormWindow
{
    private readonly R.VocabularyUsagePattern _pattern;
    private readonly TextBox _template = new() { Name = "patternTemplate", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 };
    private readonly TextBox _note = new() { Name = "patternNote", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 };
    private readonly TextBox _region = new() { Name = "patternRegion" }, _collocations = new() { Name = "patternCollocations", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 };
    private readonly ComboBox _formality = new() { Name = "patternFormality", ItemsSource = Enum.GetValues<R.VocabularyPatternFormality>() }, _medium = new() { Name = "patternMedium", ItemsSource = Enum.GetValues<R.VocabularyPatternMedium>() };
    protected override bool Valid => !string.IsNullOrWhiteSpace(_template.Text);
    internal FrozenVocabularyUsagePatternEditorWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, Guid senseId, R.VocabularyUsagePattern? pattern = null)
        : base(store, baseline, "Edit usage pattern")
    {
        _pattern = pattern ?? new(senseId, ""); _template.Text = _pattern.Template; _template.IsReadOnly = _pattern.Slots.Count > 0; _note.Text = _pattern.Note; _region.Text = _pattern.Region; _collocations.Text = string.Join("\n", _pattern.Collocations); _formality.SelectedItem = _pattern.Formality; _medium.SelectedItem = _pattern.Medium;
        Fields.Children.Add(VocabularyInputControls.Group(("Usage pattern", _template), ("Usage note", _note)));
        var details = new Expander { Name = "patternDetails", Header = "Details", Margin = new Thickness(0, 12, 0, 0), Content = VocabularyInputControls.Group(("Formality", _formality), ("Medium", _medium), ("Region or dialect", _region), ("Collocations", _collocations)) }; VocabularyNotebookTheme.Brush(details, Control.ForegroundProperty, "TextPrimary"); Fields.Children.Add(details);
        _template.TextChanged += (_, _) => UpdateAdmission(); UpdateAdmission();
    }
    protected override VocabularySemanticIndex Edited()
    {
        var value = _pattern with { Template = _template.Text, Note = _note.Text, Formality = (R.VocabularyPatternFormality)_formality.SelectedItem, Medium = (R.VocabularyPatternMedium)_medium.SelectedItem, Region = _region.Text, Collocations = _collocations.Text.Split(['\r', '\n', '\v', '\f', '\u0085', '\u2028', '\u2029'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray() };
        return Baseline with { Patterns = Baseline.Patterns.Any(p => p.Id == value.Id) ? Baseline.Patterns.Select(p => p.Id == value.Id ? value : p).ToArray() : Baseline.Patterns.Append(value).ToArray() };
    }
}
