using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.UI.Review;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Review;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Notebook-owned review section. It borrows the app's one review Feature and Store,
/// admits every asynchronous operation through that Feature, and retires its child before either
/// shared owner is disposed. The notebook supplies fresh entry/sense values when rendering.</summary>
internal sealed class FrozenVocabularyReviewSection
{
    private readonly Window _owner;
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
    internal Task? LastOperation => _operation;
    internal CanonicalAnkiReviewWindow? CardWindow => _window;
    internal FrozenVocabularyReviewSection(Window owner, ICanonicalVocabularyStore store, AnkiReviewFeature feature, Action configure, Action changed, Func<string, bool>? confirm = null, Func<Guid, Guid, bool>? canAct = null)
    { _owner = owner; _store = store; _feature = feature; _configure = configure; _changed = changed; _canAct = canAct ?? ((_, _) => true); _confirm = confirm ?? (name => MessageBox.Show(owner, $"Remove local card ‘{name}’?", "Remove card", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK); }

    internal FrameworkElement Render(VocabularyEntry entry, VocabularySense sense)
    {
        var panel = new StackPanel(); var header = new DockPanel();
        var add = Button("Add card", () => Open(entry.Id, sense.Id, Guid.NewGuid())); add.IsEnabled = _canAct(entry.Id, sense.Id) && !_retiring && _operation is null && _window is null && _feature.Controller.SendingSenseId is null;
        DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        header.Children.Add(new TextBlock { Text = $"Review cards ({sense.ReviewCards?.Count ?? 0})", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(header);
        foreach (var card in sense.ReviewCards ?? [])
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 4) }; string name = card.Plan.Name ?? "Review";
            row.Children.Add(new TextBlock { Text = name + "  ·  " + _feature.Controller.CanonicalStatus(entry, sense, card.Plan.Id), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (card.Plan.Layout is { } layout) row.Children.Add(VocabularyInputControls.Caption(string.Join(" + ", layout.Front.Select(p => p.Source.Label())) + " → " + string.Join(" + ", layout.Back.Select(p => p.Source.Label()))));
            var actions = new WrapPanel(); bool enabled = _canAct(entry.Id, sense.Id) && !_retiring && _operation is null && _window is null && _feature.Controller.SendingSenseId is null;
            var edit = Button("Edit", () => Open(entry.Id, sense.Id, card.Plan.Id)); edit.IsEnabled = enabled; actions.Children.Add(edit);
            if (card.ActiveBinding is { NoteId: not null } binding)
            {
                var open = Button("View in Anki", () => Navigate(entry.Id, sense.Id, token => _feature.Controller.OpenCanonicalAsync(binding, token))); open.IsEnabled = enabled; actions.Children.Add(open);
                var review = Button("Review in Anki", () => Navigate(entry.Id, sense.Id, token => _feature.Controller.ReviewCanonicalDeckAsync(binding, token))); review.IsEnabled = enabled; actions.Children.Add(review);
            }
            var remove = Button("Remove local card", () => { if (_canAct(entry.Id, sense.Id) && _confirm(name) && _canAct(entry.Id, sense.Id)) Start(async token => { await _store.RemoveReviewAsync(entry.Id, sense.Id, card.Plan.Id, card, token); }); }); remove.IsEnabled = enabled; actions.Children.Add(remove); row.Children.Add(actions); panel.Children.Add(row);
        }
        if (_error is not null) panel.Children.Add(VocabularyNotebookTheme.Brush(new TextBlock { Text = _error, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush"));
        return VocabularyInputControls.Group(("", panel));
    }
    private static Button Button(string title, Action action)
    { var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) }; VocabularyNotebookTheme.Button(button); button.Click += (_, _) => action(); return button; }
    private void Open(Guid entryId, Guid senseId, Guid cardId)
    {
        if (!_canAct(entryId, senseId)) return;
        if (_window is not null) { _window.Activate(); return; }
        Start(async token =>
        {
            var session = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store, entryId, senseId, cardId, token);
            if (_retiring || !_canAct(entryId, senseId)) { session.Dispose(); return; }
            try
            {
                var child = new CanonicalAnkiReviewWindow(session, _feature.RunAsync, _configure) { Owner = _owner }; _window = child;
                child.Closed += (_, _) => { if (ReferenceEquals(_window, child)) _window = null; if (!_retiring) _changed(); };
                child.Show(); child.Activate();
            }
            catch { session.Dispose(); throw; }
        });
    }
    private void Navigate(Guid entryId, Guid senseId, Func<CancellationToken, Task<string?>> navigate) { if (_canAct(entryId, senseId)) Start(async token => { _error = await navigate(token); }); }
    private void Start(Func<CancellationToken, Task> action)
    {
        if (_retiring || _operation is not null) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _operation = completion.Task; _error = null; _changed();
        _ = Run(); async Task Run()
        {
            try { await _feature.RunAsync(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, _lifetime.Token); await action(linked.Token); }); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_retiring) _error = error.Message; }
            finally { _operation = null; completion.TrySetResult(); if (!_retiring) _changed(); }
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
        try { if (_operation is { } operation) await operation; } catch (Exception error) { errors.Add(error); }
        try { if (_window is { } child) await child.RetireAsync(); } catch (Exception error) { errors.Add(error); }
        _lifetime.Dispose(); if (errors.Count == 0) completion.TrySetResult(); else completion.TrySetException(errors);
    }
}
