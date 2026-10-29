using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Semantic dialogs and commands use detached index baselines and the one borrowed owner.
/// Modal and direct accepted writes drain before the window unsubscribes from that owner.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private bool _semanticBusy;
    private TaskCompletionSource? _semanticCompletion;
    private bool CanSemantic => !_retiring && !_senseMutating && !_semanticBusy && _modalDone is null && IsVisible && _store is ICanonicalVocabularySemanticStore && _session.Snapshot is not null;
    private Button NewMeaningButton(VocabularySelection? link = null)
    {
        long epoch = _actionSelectionEpoch;
        var button = SenseButton("New meaning", "canonicalNewMeaning", CanSemantic && _store is ICanonicalVocabularyMeaningOrganizationStore); button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMeaningAsync(null, link)); }; return button;
    }
    private void AddSenseSemanticActions(Panel panel, VocabularySelection selection)
    {
        long epoch = _actionSelectionEpoch;
        var group = SenseButton("Group by meaning", "canonicalGroupSense", CanSemantic && _session.Snapshot!.Document.Semantics.Nodes.Count > 0);
        group.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(null, selection.SenseId, null)); }; panel.Children.Add(group);
        panel.Children.Add(NewMeaningButton(selection));
        var pattern = SenseButton("Add usage pattern", "canonicalAddPattern", CanSemantic); pattern.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenPatternAsync(selection.SenseId, null)); }; panel.Children.Add(pattern);
    }
    private void AddMeaningActions(Panel panel, F.VocabularyMeaningNode node)
    {
        long epoch = _actionSelectionEpoch;
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var edit = SenseButton("Edit meaning", "canonicalEditMeaning", CanSemantic && _store is ICanonicalVocabularyMeaningOrganizationStore); edit.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMeaningAsync(node, null)); }; buttons.Children.Add(edit);
        var delete = SenseButton("Delete meaning", "canonicalDeleteMeaning", CanSemantic); delete.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(DeleteMeaningAsync(node.Id)); }; buttons.Children.Add(delete);
        var add = SenseButton("Add from vocabulary", "canonicalAddMapping", CanSemantic && _session.Entries.Count > 0); add.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(node.Id, null, null)); }; buttons.Children.Add(add);
        var categorize = SenseButton("Categorize", "canonicalCategorizeMeaning", CanSemantic); categorize.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenCategoriesAsync(node.Id)); }; buttons.Children.Add(categorize);
        panel.Children.Add(buttons);
        if (_semanticBusy) panel.Children.Add(Caption("Updating vocabulary…"));
    }
    private void AddMeaningCategoryLinks(Panel panel, F.VocabularyMeaningNode node)
    {
        long epoch = _actionSelectionEpoch; var index = _session.Snapshot!.Document.Classification; var ids = index.Memberships.Where(m => m.NodeId == node.Id && m.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(m => m.CategoryId).ToHashSet(); var categories = index.Categories.Where(c => ids.Contains(c.Id) && !c.IsRetired).ToArray(); if (categories.Length == 0) return;
        Section(panel, "Categories"); var links = new WrapPanel(); foreach (var category in categories) { var label = string.Join(" › ", Cotabby.Core.Vocabulary.Notebook.VocabularyClassificationProjection.Breadcrumb(category.Id, index).TakeLast(2).Select(c => R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(c, index), [VocabularyCategoryUiLanguage.Identifier]).Name)); var link = SenseButton(label, "meaningCategoryLink", true); link.Click += (_, _) => { if (epoch == _actionSelectionEpoch) { ++_actionSelectionEpoch; _session.Destination = new(VocabularyMeaningDestinationKind.Category, CategoryId: category.Id); _session.Refresh(); } }; links.Children.Add(link); } panel.Children.Add(links);
    }
    private void CategoryDetail(StackPanel panel, R.VocabularyCategory category)
    {
        long epoch = _actionSelectionEpoch; var index = _session.Snapshot!.Document.Classification; var effective = R.VocabularyClassificationPolicy.Effective(category, index); var presentation = R.VocabularyCategoryLocalizationPolicy.Resolve(effective, [VocabularyCategoryUiLanguage.Identifier]);
        panel.Children.Add(Caption(category.Axis == R.VocabularyCategoryAxis.Content ? "Content meanings" : "Communicative functions")); var ancestors = Cotabby.Core.Vocabulary.Notebook.VocabularyClassificationProjection.Breadcrumb(category.Id, index).SkipLast(1).ToArray(); if (ancestors.Length > 0) { var trail = new WrapPanel(); foreach (var ancestor in ancestors) { var label = R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(ancestor, index), [VocabularyCategoryUiLanguage.Identifier]).Name; var button = SenseButton("› " + label, "categoryAncestor", true); button.Click += (_, _) => { ++_actionSelectionEpoch; _session.Destination = new(VocabularyMeaningDestinationKind.Category, CategoryId: ancestor.Id); _session.Refresh(); }; trail.Children.Add(button); } panel.Children.Add(trail); } Heading(panel, presentation.Name);
        if (presentation.Definition.Length > 0) panel.Children.Add(Paragraph(presentation.Definition));
        var buttons = new WrapPanel(); var addMeaning = SenseButton("New meaning", "categoryDetailNewMeaning", CanSemantic && _store is ICanonicalVocabularyMeaningOrganizationStore); addMeaning.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMeaningAsync(null, null, category.Id)); }; buttons.Children.Add(addMeaning);
        var edit = SenseButton("Edit category", "categoryDetailEdit", CanSemantic); edit.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenCategoryEditorAsync(category.Id, null)); }; buttons.Children.Add(edit);
        var create = SenseButton("New category", "categoryDetailNewCategory", CanSemantic); create.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenCategoryEditorAsync(null, category.Id)); }; buttons.Children.Add(create); panel.Children.Add(buttons);
        var children = index.Parents.Where(x => x.ParentId == category.Id).Select(x => x.CategoryId).ToHashSet(); var visible = index.Categories.Where(x => children.Contains(x.Id) && !x.IsRetired && !index.Overrides.Any(o => o.CategoryId == x.Id && o.IsHidden)).ToArray();
        if (visible.Length > 0) { Section(panel, "Subcategories"); var row = new WrapPanel(); foreach (var child in visible) { var name = R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(child, index), [VocabularyCategoryUiLanguage.Identifier]).Name; var button = SenseButton(name, "categoryDetailChild", true); button.Click += (_, _) => { ++_actionSelectionEpoch; _session.Destination = new(VocabularyMeaningDestinationKind.Category, CategoryId: child.Id); _session.Refresh(); }; row.Children.Add(button); } panel.Children.Add(row); }
        Section(panel, "Meaning groups"); var ids = R.VocabularyClassificationPolicy.MeaningIds(category.Id, index); var meanings = Cotabby.Core.Vocabulary.Notebook.VocabularyMeaningBrowserProjection.Items(_session.Snapshot.Document.Semantics, _session.Scoped, new Search(), StringComparer.CurrentCultureIgnoreCase, includeEmpty: true, preferredLanguages: _session.PreferredLanguages).Where(x => ids.Contains(x.Id)).ToArray(); if (meanings.Length == 0) panel.Children.Add(Caption("Unexplored"));
        foreach (var item in meanings) { var button = SenseButton(item.Title, "categoryDetailMeaning", true); button.HorizontalAlignment = HorizontalAlignment.Left; button.Click += (_, _) => { ++_actionSelectionEpoch; ShowMeaning(item.Id); }; panel.Children.Add(button); }
    }
    private void AddCardSemanticActions(Panel panel, VocabularySelection selection)
    {
        if (_session.Destination is { Kind: VocabularyMeaningDestinationKind.Unclassified })
        {
            long epoch = _actionSelectionEpoch;
            var group = SenseButton("Group by meaning", "canonicalGroupUnclassified", CanSemantic && _session.Snapshot!.Document.Semantics.Nodes.Count > 0); group.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(null, selection.SenseId, null)); }; panel.Children.Add(group);
            panel.Children.Add(NewMeaningButton(selection)); return;
        }
        if (_session.Destination?.NodeId is not { } node || _session.Snapshot is not { } snapshot) return;
        foreach (var mapping in snapshot.Document.Semantics.Mappings.Where(m => m.NodeId == node && m.SenseId == selection.SenseId))
            AddMappingButtons(panel, mapping);
    }
    private void AddSenseMappingSection(Panel panel, VocabularySelection selection)
    {
        if (_session.Snapshot is not { } snapshot) return;
        var mappings = snapshot.Document.Semantics.Mappings.Where(m => m.SenseId == selection.SenseId).ToArray();
        if (mappings.Length > 0) Section(panel, "Meaning groups");
        foreach (var mapping in mappings)
        {
            var node = snapshot.Document.Semantics.Nodes.FirstOrDefault(n => n.Id == mapping.NodeId); if (node is null) continue;
            var open = SenseButton(Cotabby.Core.Vocabulary.Notebook.VocabularyMeaningLabelPolicy.Title(node, _session.PreferredLanguages), "canonicalOpenMeaning", true); open.Click += (_, _) => { ++_actionSelectionEpoch; ShowMeaning(node.Id); }; panel.Children.Add(open);
            if (mapping.Provenance.Status == R.VocabularySemanticStatus.Proposed) panel.Children.Add(Caption("Suggested grouping"));
            if (mapping.Conditions.Length > 0) panel.Children.Add(Caption(mapping.Conditions)); if (mapping.Nuance.Length > 0) panel.Children.Add(Caption(mapping.Nuance));
            if (mapping.PatternId is { } id && snapshot.Document.Semantics.Patterns.FirstOrDefault(p => p.Id == id) is { } pattern) { panel.Children.Add(Caption("Used in this wording:")); AddPatternSummary(panel, pattern, false, mapping.Provenance.Status != R.VocabularySemanticStatus.Proposed); }
            AddMappingButtons(panel, mapping);
        }
        var patterns = snapshot.Document.Semantics.Patterns.Where(p => p.SenseId == selection.SenseId).ToArray();
        var standalone = patterns.Where(p => !mappings.Any(m => m.PatternId == p.Id)).ToArray();
        if (standalone.Length > 0) { Section(panel, "Usage patterns"); foreach (var pattern in standalone) AddPatternSummary(panel, pattern, true); }
    }
    private void AddPatternSummary(Panel panel, R.VocabularyUsagePattern pattern, bool standalone, bool allowEdit = true)
    {
        long epoch = _actionSelectionEpoch; panel.Children.Add(Paragraph(pattern.Template));
        var details = new[] { pattern.Formality == R.VocabularyPatternFormality.Unspecified ? "" : pattern.Formality.ToString(), pattern.Medium == R.VocabularyPatternMedium.Unspecified ? "" : pattern.Medium.ToString(), pattern.Region.Trim() }.Where(s => s.Length > 0);
        var badges = string.Join(" · ", details); if (badges.Length > 0) panel.Children.Add(Caption(badges));
        if (pattern.Note.Length > 0) panel.Children.Add(Caption(pattern.Note)); if (pattern.Collocations.Count > 0) panel.Children.Add(Caption(string.Join(" · ", pattern.Collocations))); if (pattern.Provenance.Status == R.VocabularySemanticStatus.Proposed) panel.Children.Add(Caption("Suggested wording"));
        var actions = new WrapPanel(); if (allowEdit) { var edit = SenseButton("Edit usage pattern", "canonicalEditPattern", CanSemantic); edit.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenPatternAsync(pattern.SenseId, pattern)); }; actions.Children.Add(edit); }
        if (standalone) { var remove = SenseButton("Remove wording", "canonicalRemovePattern", CanSemantic); remove.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(DeletePatternAsync(pattern.Id)); }; actions.Children.Add(remove); } panel.Children.Add(actions);
    }
    private async Task OpenPatternAsync(Guid senseId, R.VocabularyUsagePattern? pattern)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot || !_session.Entries.Any(e => e.Senses.Any(s => s.Id == senseId))) return;
        if (pattern is not null && !snapshot.Document.Semantics.Patterns.Contains(pattern)) return;
        await OpenSemanticFormAsync(new FrozenVocabularyUsagePatternEditorWindow(store, snapshot.Document.Semantics, senseId, pattern) { Owner = this }, null);
    }
    private async Task DeletePatternAsync(Guid id)
    {
        if (!CanSemantic) return; long epoch = _actionSelectionEpoch; var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); bool confirm = false;
        try { var dialog = new FrozenVocabularyPatternDeleteWindow { Owner = this }; _inputWindow = dialog; try { confirm = dialog.ShowDialog() == true; } finally { _inputWindow = null; } }
        finally { _modalDone = null; done.TrySetResult(); }
        if (confirm && CanSemantic && epoch == _actionSelectionEpoch && _session.Snapshot is { } snapshot && snapshot.Document.Semantics.Patterns.Any(p => p.Id == id))
        { var baseline = snapshot.Document.Semantics; await MutateSemanticsAsync(VocabularySemanticFormPolicy.RemovePattern(baseline, id), baseline); }
        if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh();
    }
    private void AddMappingButtons(Panel panel, R.VocabularyMeaningMapping mapping)
    {
        long epoch = _actionSelectionEpoch;
        var buttons = new WrapPanel();
        if (mapping.Provenance.Status == R.VocabularySemanticStatus.Proposed)
        {
            var confirm = SenseButton("Use this grouping", "canonicalConfirmMapping", CanSemantic); confirm.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(UpdateMappingAsync(mapping, true)); }; buttons.Children.Add(confirm);
        }
        var edit = SenseButton("Change grouping", "canonicalEditMapping", CanSemantic); edit.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(null, null, mapping)); }; buttons.Children.Add(edit);
        var remove = SenseButton("Remove from this group", "canonicalRemoveMapping", CanSemantic); remove.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(UpdateMappingAsync(mapping, false)); }; buttons.Children.Add(remove); panel.Children.Add(buttons);
    }
    private async Task OpenMeaningAsync(F.VocabularyMeaningNode? node, VocabularySelection? link, Guid? categoryId = null)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _store is not ICanonicalVocabularyMeaningOrganizationStore || _session.Snapshot is not { } snapshot) return;
        if (link is { } selection && !_session.Entries.Any(e => e.Id == selection.EntryId && e.Senses.Any(s => s.Id == selection.SenseId))) return;
        if (node is not null) { node = snapshot.Document.Semantics.Nodes.FirstOrDefault(n => n.Id == node.Id); if (node is null) return; }
        var form = new FrozenVocabularyMeaningEditorWindow(store, snapshot.Document.Semantics, _session.PreferredLanguages, node, link, snapshot.Document.Classification, _session.Entries, categoryId) { Owner = this };
        await OpenSemanticFormAsync(form, node is null ? form.NodeId : null);
    }
    private async Task OpenMappingAsync(Guid? node, Guid? sense, R.VocabularyMeaningMapping? mapping)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot) return;
        if (node is { } id && !snapshot.Document.Semantics.Nodes.Any(n => n.Id == id)) return;
        if (sense is { } senseId && !_session.Entries.Any(e => e.Senses.Any(s => s.Id == senseId))) return;
        if (mapping is not null && !snapshot.Document.Semantics.Mappings.Contains(mapping)) return;
        var form = new FrozenVocabularyMappingEditorWindow(store, snapshot.Document.Semantics, _session.Entries, _session.PreferredLanguages, node, sense, mapping, currentRules: true) { Owner = this };
        await OpenSemanticFormAsync(form, null);
    }
    private async Task OpenCategoriesAsync(Guid node)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot || !snapshot.Document.Semantics.Nodes.Any(n => n.Id == node)) return;
        var baseline = snapshot.Document.Classification; var meaning = snapshot.Document.Semantics.Nodes.Single(n => n.Id == node); var selected = baseline.Memberships.Where(m => m.NodeId == node).Select(m => m.CategoryId).ToHashSet(); var axis = meaning.Kind == R.VocabularyMeaningNodeKind.Function ? R.VocabularyCategoryAxis.Function : R.VocabularyCategoryAxis.Content;
        var form = new FrozenVocabularyDraftPickerWindow(store, baseline, _session.PreferredLanguages, selected, axis, commit: async (directory, ids) => { var edited = VocabularyClassificationFormPolicy.SelectMemberships(directory, node, ids); await store.UpdateClassificationAsync(edited, baseline, CancellationToken.None); }) { Owner = this };
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring && form.Accepted) await _session.LoadAsync();
            if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        }
        finally { _inputWindow = null; _modalDone = null; try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); } }
    }
    private async Task OpenCategoryEditorAsync(Guid? id, Guid? parent)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot) return;
        var category = id is { } target ? snapshot.Document.Classification.Categories.FirstOrDefault(c => c.Id == target && !c.IsRetired) : null;
        if (id is not null && category is null) return;
        if (parent is { } parentId && !snapshot.Document.Classification.Categories.Any(c => c.Id == parentId && !c.IsRetired)) parent = null;
        long epoch = _actionSelectionEpoch; var form = new FrozenVocabularyCategoryEditorWindow(store, snapshot.Document.Classification, _session.PreferredLanguages, category, parent, currentRules: true) { Owner = this };
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring && form.Receipt is not null)
            {
                await _session.LoadAsync();
                if (!_retiring && id is null && epoch == _actionSelectionEpoch && _session.Snapshot is { } current)
                {
                    var index = current.Document.Classification; if (index.Categories.Any(c => c.Id == form.CategoryId && !c.IsRetired) && !index.Overrides.Any(o => o.CategoryId == form.CategoryId && o.IsHidden)) _session.Destination = new(VocabularyMeaningDestinationKind.Category, CategoryId: form.CategoryId);
                    _session.Refresh();
                }
            }
            if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        }
        finally { _inputWindow = null; _modalDone = null; try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); } }
    }
    private async Task OpenCategoryManagerAsync()
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot) return;
        var form = new FrozenVocabularyCategoryManagerWindow(store, _store, snapshot, _session.PreferredLanguages, currentRules: true) { Owner = this };
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring) await _session.LoadAsync();
            if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        }
        finally { _inputWindow = null; _modalDone = null; try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); } }
    }
    private async Task OpenSemanticFormAsync(FrozenVocabularySemanticFormWindow form, Guid? revealNode)
    {
        long epoch = _actionSelectionEpoch; var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring && form.Saved) { await _session.LoadAsync(); if (!_retiring && epoch == _actionSelectionEpoch && revealNode is { } id && _session.Snapshot!.Document.Semantics.Nodes.Any(n => n.Id == id)) ShowMeaning(id); }
            if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        }
        finally { _inputWindow = null; _modalDone = null; try { if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); } }
    }
    private void ShowMeaning(Guid node)
    {
        _session.MeaningMode = true; if (!_session.MeaningItems.Any(x => x.Id == node)) _session.Category = "all";
        _session.Destination = new(VocabularyMeaningDestinationKind.Node, node); _session.Refresh();
    }
    private async Task UpdateMappingAsync(R.VocabularyMeaningMapping mapping, bool confirm)
    {
        if (!CanSemantic || _session.Snapshot is not { } snapshot) return; var baseline = snapshot.Document.Semantics;
        if (!baseline.Mappings.Contains(mapping)) return;
        var edited = baseline with { Mappings = confirm ? baseline.Mappings.Select(m => m.Id == mapping.Id ? m with { Provenance = m.Provenance with { Status = R.VocabularySemanticStatus.Confirmed } } : m).ToArray() : baseline.Mappings.Where(m => m.Id != mapping.Id).ToArray() };
        await MutateSemanticsAsync(edited, baseline);
    }
    private async Task DeleteMeaningAsync(Guid node)
    {
        if (!CanSemantic) return; long epoch = _actionSelectionEpoch; var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); bool confirm = false;
        try { var dialog = new FrozenVocabularySemanticDeleteWindow { Owner = this }; _inputWindow = dialog; try { confirm = dialog.ShowDialog() == true; } finally { _inputWindow = null; } }
        finally { _modalDone = null; done.TrySetResult(); }
        if (confirm && CanSemantic && epoch == _actionSelectionEpoch && _session.Snapshot is { } snapshot && snapshot.Document.Semantics.Nodes.Any(n => n.Id == node))
        { var baseline = snapshot.Document.Semantics; await MutateSemanticsAsync(VocabularySemanticFormPolicy.RemoveNode(baseline, node), baseline); }
        if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); }
        if (!_retiring) _session.Refresh();
    }
    private async Task MutateSemanticsAsync(F.VocabularySemanticIndex edited, F.VocabularySemanticIndex baseline)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store) return;
        long epoch = _actionSelectionEpoch; _semanticBusy = true; var done = _semanticCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); _actionError = null;
        try { _session.Refresh(); await store.UpdateSemanticsAsync(edited, baseline, CancellationToken.None); if (!_retiring) await _session.LoadAsync(); }
        catch (Exception e) { if (!_retiring && epoch == _actionSelectionEpoch) _actionError = e.Message; }
        finally { _semanticBusy = false; _semanticCompletion = null; try { if (!_retiring) _session.Refresh(); } finally { done.TrySetResult(); } }
    }
}

internal sealed class FrozenVocabularyPatternDeleteWindow : Window
{
    internal FrozenVocabularyPatternDeleteWindow()
    {
        Title = "Vocabulary"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; VocabularyNotebookTheme.Follow(this);
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root; root.Children.Add(new TextBlock { Text = "Remove this wording and its groupings?", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var cancel = new Button { Name = "patternDeleteCancel", Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90 }; var remove = new Button { Name = "patternDeleteConfirm", Content = "Remove", MinWidth = 90 }; cancel.Click += (_, _) => DialogResult = false; remove.Click += (_, _) => DialogResult = true; root.Children.Add(VocabularyInputControls.Footer(cancel, remove));
    }
}

internal sealed class FrozenVocabularySemanticDeleteWindow : Window
{
    internal FrozenVocabularySemanticDeleteWindow()
    {
        Title = "Vocabulary"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; VocabularyNotebookTheme.Follow(this);
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root; root.Children.Add(new TextBlock { Text = "Delete this meaning?", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); root.Children.Add(VocabularyInputControls.Caption("Collected expressions will be kept."));
        var cancel = new Button { Name = "semanticDeleteCancel", Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90 }; var delete = new Button { Name = "semanticDeleteConfirm", Content = "Delete meaning", MinWidth = 90 }; cancel.Click += (_, _) => DialogResult = false; delete.Click += (_, _) => DialogResult = true; root.Children.Add(VocabularyInputControls.Footer(cancel, delete));
    }
}
