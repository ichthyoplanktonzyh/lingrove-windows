using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>One classification membership draft. Nested creation adopts the acknowledged current
/// directory but retains the old membership baseline, so concurrent additions are not deleted.</summary>
internal sealed class FrozenVocabularyMeaningCategoryWindow : FrozenVocabularyClassificationWindow
{
    private readonly IVocabularyEntryImportStore _reader;
    private readonly IReadOnlyList<LearningLanguage> _languages;
    private readonly Guid _node;
    private readonly HashSet<Guid> _selected;
    private readonly TextBox _query = new() { Name = "categoryMembershipSearch" };
    private readonly ListBox _content = new() { Name = "categoryContentChoices", Height = 145 }, _functions = new() { Name = "categoryFunctionChoices", Height = 145 };
    protected override bool Valid => true;
    internal FrozenVocabularyMeaningCategoryWindow(ICanonicalVocabularySemanticStore store, IVocabularyEntryImportStore reader, R.VocabularyClassificationIndex baseline, Guid node, IReadOnlyList<LearningLanguage> languages)
        : base(store, baseline, "Categorize")
    {
        _reader = reader; _node = node; _languages = Array.AsReadOnly(languages.ToArray()); _selected = baseline.Memberships.Where(m => m.NodeId == node).Select(m => m.CategoryId).ToHashSet();
        _query.Tag = "Search categories"; VocabularyNotebookTheme.Input(_query); System.Windows.Automation.AutomationProperties.SetName(_query, "Search categories");
        var create = new Button { Name = "membershipNewCategory", Content = "New category", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5) }; VocabularyNotebookTheme.Button(create); create.Click += (_, _) => _ = CreateAsync();
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(create, Dock.Right); row.Children.Add(create); row.Children.Add(_query); Fields.Children.Add(row);
        Fields.Children.Add(VocabularyInputControls.Heading("Content meanings")); Fields.Children.Add(_content); Fields.Children.Add(VocabularyInputControls.Heading("Communicative functions")); Fields.Children.Add(_functions);
        System.Windows.Automation.AutomationProperties.SetName(_content, "Content meanings"); System.Windows.Automation.AutomationProperties.SetName(_functions, "Communicative functions");
        foreach (var list in new[] { _content, _functions }) { list.ItemTemplate = FrozenVocabularyCategoryChoice.Template(); list.Margin = new Thickness(0, 0, 0, 12); VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); VocabularyNotebookTheme.Brush(list, Control.BackgroundProperty, "CardBg"); VocabularyNotebookTheme.Brush(list, Control.ForegroundProperty, "TextPrimary"); VocabularyNotebookTheme.Brush(list, Control.BorderBrushProperty, "CardBorder"); }
        _query.TextChanged += (_, _) => Render(); Render(); UpdateAdmission();
    }
    private void Render()
    {
        foreach (var (list, axis) in new[] { (_content, R.VocabularyCategoryAxis.Content), (_functions, R.VocabularyCategoryAxis.Function) })
            list.ItemsSource = Baseline.Categories.Where(c => c.Axis == axis && (!c.IsRetired || _selected.Contains(c.Id)))
                .Select(c => (category: c, title: R.VocabularyClassificationPolicy.Title(c, Baseline, _languages)))
                .Where(item => CultureInfo.CurrentCulture.CompareInfo.IndexOf(item.title, _query.Text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                .Select(item => new FrozenVocabularyCategoryChoice(item.title, item.category.Id, _selected)).ToArray();
    }
    protected override R.VocabularyClassificationIndex Edited() => VocabularyClassificationFormPolicy.SelectMemberships(Baseline, _node, _selected);
    private async Task CreateAsync()
    {
        if (Retiring || ChildDone is not null) return; var done = ChildDone = new(TaskCreationOptions.RunContinuationsAsynchronously); UpdateAdmission();
        try
        {
            var editor = new FrozenVocabularyCategoryEditorWindow(Store, Baseline, _languages) { Owner = this }; Child = editor;
            try { editor.ShowDialog(); } finally { await editor.RetireAsync(); Child = null; }
            if (!Retiring && editor.Receipt is not null)
            {
                var current = await _reader.ReadAsync(Lifetime.Token);
                if (!Retiring) { Baseline = VocabularyClassificationFormPolicy.AdoptDirectory(Baseline, current.Document.Classification); _selected.Add(editor.CategoryId); Render(); }
            }
        }
        catch (OperationCanceledException) when (Retiring) { }
        catch (Exception error) { if (!Retiring) Failure.Text = error.Message; }
        finally { Child = null; ChildDone = null; try { UpdateAdmission(); } finally { done.TrySetResult(); } }
    }
}
