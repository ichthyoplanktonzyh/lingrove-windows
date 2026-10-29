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
        var button = SenseButton("New meaning", "canonicalNewMeaning", CanSemantic); button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMeaningAsync(null, link)); }; return button;
    }
    private void AddSenseSemanticActions(Panel panel, VocabularySelection selection)
    {
        long epoch = _actionSelectionEpoch;
        var group = SenseButton("Group by meaning", "canonicalGroupSense", CanSemantic && _session.Snapshot!.Document.Semantics.Nodes.Count > 0);
        group.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(null, selection.SenseId, null)); }; panel.Children.Add(group);
        panel.Children.Add(NewMeaningButton(selection));
        var pattern = Disabled("Add usage pattern", "Usage pattern editing is not available in this notebook yet."); pattern.Margin = new Thickness(0, 0, 8, 8); panel.Children.Add(pattern);
    }
    private void AddMeaningActions(Panel panel, F.VocabularyMeaningNode node)
    {
        long epoch = _actionSelectionEpoch;
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var edit = SenseButton("Edit meaning", "canonicalEditMeaning", CanSemantic); edit.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMeaningAsync(node, null)); }; buttons.Children.Add(edit);
        var delete = SenseButton("Delete meaning", "canonicalDeleteMeaning", CanSemantic); delete.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(DeleteMeaningAsync(node.Id)); }; buttons.Children.Add(delete);
        var add = SenseButton("Add from vocabulary", "canonicalAddMapping", CanSemantic && _session.Entries.Count > 0); add.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenMappingAsync(node.Id, null, null)); }; buttons.Children.Add(add);
        var categorize = SenseButton("Categorize", "canonicalCategorizeMeaning", CanSemantic); categorize.Click += (_, _) => { if (epoch == _actionSelectionEpoch) LastOperation = Track(OpenCategoriesAsync(node.Id)); }; buttons.Children.Add(categorize);
        panel.Children.Add(buttons);
        if (_semanticBusy) panel.Children.Add(Caption("Updating vocabulary…"));
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
            if (mapping.PatternId is { } id && snapshot.Document.Semantics.Patterns.FirstOrDefault(p => p.Id == id) is { } pattern) panel.Children.Add(Caption("Used in this wording: " + pattern.Template));
            AddMappingButtons(panel, mapping);
        }
        var patterns = snapshot.Document.Semantics.Patterns.Where(p => p.SenseId == selection.SenseId).ToArray();
        if (patterns.Length > 0) { Section(panel, "Usage patterns"); foreach (var pattern in patterns) { panel.Children.Add(Paragraph(pattern.Template)); if (pattern.Note.Length > 0) panel.Children.Add(Caption(pattern.Note)); if (pattern.Provenance.Status == R.VocabularySemanticStatus.Proposed) panel.Children.Add(Caption("Suggested wording")); } }
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
    private async Task OpenMeaningAsync(F.VocabularyMeaningNode? node, VocabularySelection? link)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot) return;
        if (link is { } selection && !_session.Entries.Any(e => e.Id == selection.EntryId && e.Senses.Any(s => s.Id == selection.SenseId))) return;
        if (node is not null) { node = snapshot.Document.Semantics.Nodes.FirstOrDefault(n => n.Id == node.Id); if (node is null) return; }
        var form = new FrozenVocabularyMeaningEditorWindow(store, snapshot.Document.Semantics, _session.PreferredLanguages, node, link) { Owner = this };
        await OpenSemanticFormAsync(form, node is null ? form.NodeId : null);
    }
    private async Task OpenMappingAsync(Guid? node, Guid? sense, R.VocabularyMeaningMapping? mapping)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot) return;
        if (node is { } id && !snapshot.Document.Semantics.Nodes.Any(n => n.Id == id)) return;
        if (sense is { } senseId && !_session.Entries.Any(e => e.Senses.Any(s => s.Id == senseId))) return;
        if (mapping is not null && !snapshot.Document.Semantics.Mappings.Contains(mapping)) return;
        var form = new FrozenVocabularyMappingEditorWindow(store, snapshot.Document.Semantics, _session.Entries, _session.PreferredLanguages, node, sense, mapping) { Owner = this };
        await OpenSemanticFormAsync(form, null);
    }
    private async Task OpenCategoriesAsync(Guid node)
    {
        if (!CanSemantic || _store is not ICanonicalVocabularySemanticStore store || _session.Snapshot is not { } snapshot || !snapshot.Document.Semantics.Nodes.Any(n => n.Id == node)) return;
        var form = new FrozenVocabularyMeaningCategoryWindow(store, _store, snapshot.Document.Classification, node, _session.PreferredLanguages) { Owner = this };
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring && form.Receipt is not null) await _session.LoadAsync();
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
        long epoch = _actionSelectionEpoch; var form = new FrozenVocabularyCategoryEditorWindow(store, snapshot.Document.Classification, _session.PreferredLanguages, category, parent) { Owner = this };
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously); CloseActionMenus();
        try
        {
            _inputWindow = form; try { form.ShowDialog(); } finally { await form.RetireAsync(); _inputWindow = null; }
            if (!_retiring && form.Receipt is not null)
            {
                await _session.LoadAsync();
                if (!_retiring && id is null && epoch == _actionSelectionEpoch && _session.Snapshot is { } current)
                {
                    var index = current.Document.Classification; _session.Category = index.Categories.Any(c => c.Id == form.CategoryId && !c.IsRetired) && !index.Overrides.Any(o => o.CategoryId == form.CategoryId && o.IsHidden) ? form.CategoryId.ToString("D").ToUpperInvariant() : "all";
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
        var form = new FrozenVocabularyCategoryManagerWindow(store, _store, snapshot, _session.PreferredLanguages) { Owner = this };
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

internal sealed class FrozenVocabularySemanticDeleteWindow : Window
{
    internal FrozenVocabularySemanticDeleteWindow()
    {
        Title = "Vocabulary"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; VocabularyNotebookTheme.Follow(this);
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root; root.Children.Add(new TextBlock { Text = "Delete this meaning?", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); root.Children.Add(VocabularyInputControls.Caption("Collected expressions will be kept."));
        var cancel = new Button { Name = "semanticDeleteCancel", Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90 }; var delete = new Button { Name = "semanticDeleteConfirm", Content = "Delete meaning", MinWidth = 90 }; cancel.Click += (_, _) => DialogResult = false; delete.Click += (_, _) => DialogResult = true; root.Children.Add(VocabularyInputControls.Footer(cancel, delete));
    }
}
