using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.UI.Review;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Review;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Notebook-owned review section. It borrows the app's one review Feature and Store,
/// admits every asynchronous operation through that Feature, and retires its child before either
/// shared owner is disposed. The notebook supplies fresh entry/sense values when rendering.
/// Only fixed captions and CanonicalStatus's closed stage keys enter the interface catalog;
/// card names, field values and operation diagnostics remain literal.</summary>
internal sealed class FrozenVocabularyReviewSection
{
    private readonly Window _owner;
    private readonly bool _allowExternalMedia;
    private readonly IVocabularyClipboardImageTransfer? _clipboard;
    private readonly ICanonicalVocabularyStore _store;
    private readonly AnkiReviewFeature _feature;
    private readonly Action _configure, _changed;
    private readonly Func<string, bool> _confirm;
    private readonly Func<Guid, Guid, bool> _canAct;
    private readonly CancellationTokenSource _lifetime = new();
    private CanonicalAnkiReviewWindow? _window;
    private Task? _operation, _retirement;
    private bool _retiring;
    private string? _error;
    private (Guid Entry, Guid Sense)? _errorSelection;
    internal Task? LastOperation => _operation;
    internal bool IsBusy => _operation is not null || _window is not null;
    internal CanonicalAnkiReviewWindow? CardWindow => _window;
    internal FrozenVocabularyReviewSection(Window owner, ICanonicalVocabularyStore store, AnkiReviewFeature feature, Action configure, Action changed, Func<string, bool>? confirm = null, Func<Guid, Guid, bool>? canAct = null, IVocabularyClipboardImageTransfer? clipboard = null, bool allowExternalMedia = true)
    { _allowExternalMedia = allowExternalMedia; _clipboard = clipboard; _owner = owner; _store = store; _feature = feature; _configure = configure; _changed = changed; _canAct = canAct ?? ((_, _) => true); _confirm = confirm ?? (name => MessageBox.Show(owner, InterfaceText.Get("Remove this card?", "Vocabulary") + "\n" + name, InterfaceText.Get("Remove local card", "Vocabulary"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK); }

    internal FrameworkElement Render(VocabularyEntry entry, VocabularySense sense)
    {
        var panel = new StackPanel(); var header = new DockPanel();
        var add = Button(InterfaceText.Get("Add card", "Vocabulary"), () => Open(entry.Id, sense.Id, Guid.NewGuid())); add.IsEnabled = _canAct(entry.Id, sense.Id) && !_retiring && _operation is null && _window is null && _feature.Controller.SendingSenseId is null;
        DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        header.Children.Add(new TextBlock { Text = InterfaceText.Get("Review cards", "Vocabulary") + " (" + (sense.ReviewCards?.Count ?? 0).ToString("N0", InterfaceText.Culture) + ")", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(header);
        foreach (var card in sense.ReviewCards ?? [])
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 4) }; string name = card.Plan.Name ?? InterfaceText.Get("Review", "Vocabulary");
            row.Children.Add(new TextBlock { Text = name + "  ·  " + InterfaceText.Get(_feature.Controller.CanonicalStatus(entry, sense, card.Plan.Id), "Vocabulary"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (card.Plan.Layout is { } layout) row.Children.Add(VocabularyInputControls.Caption(string.Join(" + ", layout.Front.Select(p => InterfaceText.Get(p.Source.Label(), "Vocabulary"))) + " → " + string.Join(" + ", layout.Back.Select(p => InterfaceText.Get(p.Source.Label(), "Vocabulary")))));
            var actions = new WrapPanel(); bool enabled = _canAct(entry.Id, sense.Id) && !_retiring && _operation is null && _window is null && _feature.Controller.SendingSenseId is null;
            var edit = Button(InterfaceText.Get("Edit", "Vocabulary"), () => Open(entry.Id, sense.Id, card.Plan.Id)); edit.IsEnabled = enabled; actions.Children.Add(edit);
            if (card.ActiveBinding is { NoteId: not null } binding)
            {
                var open = Button(InterfaceText.Get("View in Anki", "Vocabulary"), () => Navigate(entry.Id, sense.Id, token => _feature.Controller.OpenCanonicalAsync(binding, guardToken => BindingCurrentAsync(entry.Id, sense.Id, card.Plan.Id, binding, guardToken), token))); open.IsEnabled = enabled; actions.Children.Add(open);
                var review = Button(InterfaceText.Get("Review in Anki", "Vocabulary"), () => Navigate(entry.Id, sense.Id, token => _feature.Controller.ReviewCanonicalDeckAsync(binding, guardToken => BindingCurrentAsync(entry.Id, sense.Id, card.Plan.Id, binding, guardToken), token))); review.IsEnabled = enabled; actions.Children.Add(review);
            }
            var remove = Button(InterfaceText.Get("Remove local card", "Vocabulary"), () => { if (_canAct(entry.Id, sense.Id) && _confirm(name) && _canAct(entry.Id, sense.Id)) Start(entry.Id, sense.Id, async token => { await _store.RemoveReviewAsync(entry.Id, sense.Id, card.Plan.Id, card, token); }); }); remove.IsEnabled = enabled; actions.Children.Add(remove); row.Children.Add(actions); panel.Children.Add(row);
        }
        if (_error is not null && _errorSelection == (entry.Id, sense.Id)) panel.Children.Add(VocabularyNotebookTheme.Brush(new TextBlock { Text = _error, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush"));
        return VocabularyInputControls.Group(("", panel));
    }
    private static Button Button(string title, Action action)
    { var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) }; VocabularyNotebookTheme.Button(button); button.Click += (_, _) => action(); return button; }
    private void Notify() { try { _changed(); } catch (Exception error) { _error = InterfaceFailureText.For(error); } }
    private void Open(Guid entryId, Guid senseId, Guid cardId)
    {
        if (!_canAct(entryId, senseId) || _retiring || _operation is not null || _feature.Controller.SendingSenseId is not null) return;
        if (_window is not null) { _window.Activate(); return; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operation = completion.Task; _error = null; _errorSelection = (entryId, senseId); Notify();
        _ = Run();
        async Task Run()
        {
            CanonicalAnkiReviewSession? session = null;
            try
            {
                await _feature.RunAsync(async featureToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, _lifetime.Token);
                    session = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store, entryId, senseId, cardId, linked.Token);
                });
                if (_retiring || !_canAct(entryId, senseId)) return;
                var child = new CanonicalAnkiReviewWindow(session!, _feature.RunAsync, _configure, _clipboard, allowExternalMedia: _allowExternalMedia) { Owner = _owner }; _window = child;
                session = null; // child owns only this borrowed session, never the shared services
                child.Closed += (_, _) => { if (ReferenceEquals(_window, child)) _window = null; if (!_retiring) Notify(); };
                try { child.ShowDialog(); } finally { await child.RetireAsync(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_retiring) _error = InterfaceFailureText.For(error); }
            finally { session?.Dispose(); _operation = null; completion.TrySetResult(); if (!_retiring) Notify(); }
        }
    }
    private async Task<bool> BindingCurrentAsync(Guid entryId, Guid senseId, Guid cardId, Cotabby.Core.Vocabulary.Frozen.AnkiNoteBinding binding, CancellationToken token)
    {
        if (_retiring || !_canAct(entryId, senseId)) return false;
        var snapshot = await _store.ReadAsync(token);
        if (_retiring || !_canAct(entryId, senseId)) return false;
        var current = snapshot.Document.CollectedEntries.FirstOrDefault(entry => entry.Id == entryId)?.Senses
            .FirstOrDefault(sense => sense.Id == senseId)?.ReviewCards?.FirstOrDefault(card => card.Plan.Id == cardId);
        return current?.ActiveBindingId == binding.Id && Equals(current.ActiveBinding, binding);
    }
    private void Navigate(Guid entryId, Guid senseId, Func<CancellationToken, Task<string?>> navigate) { if (_canAct(entryId, senseId)) Start(entryId, senseId, async token => { _error = await navigate(token); }); }
    private void Start(Guid entryId, Guid senseId, Func<CancellationToken, Task> action)
    {
        if (_retiring || _operation is not null || _window is not null || _feature.Controller.SendingSenseId is not null) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _operation = completion.Task; _error = null; _errorSelection = (entryId, senseId); Notify();
        _ = Run(); async Task Run()
        {
            try { await _feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, _lifetime.Token); await action(linked.Token); }); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_retiring) _error = InterfaceFailureText.For(error); }
            finally { _operation = null; completion.TrySetResult(); if (!_retiring) Notify(); }
        }
    }
    internal Task RetireAsync()
    {
        _owner.Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _retirement = completion.Task; _ = RetireCoreAsync(completion); return completion.Task;
    }
    private async Task RetireCoreAsync(TaskCompletionSource completion)
    {
        _retiring = true; var errors = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
        try { if (_window is { } child) await child.RetireAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_operation is { } operation) await operation; } catch (Exception error) { errors.Add(error); }
        _lifetime.Dispose(); if (errors.Count == 0) completion.TrySetResult(); else completion.TrySetException(errors);
    }
}
