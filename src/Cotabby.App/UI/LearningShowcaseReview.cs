using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Review;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI;

/// <summary>The real canonical review editor/session borrows only the sample notebook and
/// in-memory Anki effects. It cannot contact a live profile or activate an external application.</summary>
internal sealed class LearningShowcaseReview : StackPanel, IAsyncDisposable
{
    private sealed class Credentials : IAnkiCredentialStore
    { public string ReadKey() => ""; public void SaveKey(string value) { } }
    private readonly LearningShowcaseStore _store;
    private readonly VocabularyCapture _capture;
    private readonly AnkiReviewFeature _feature;
    private readonly ContentControl _editor = new() { Height = 280 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private CanonicalAnkiReviewSession? _session;
    private CanonicalAnkiReviewWindow? _window;
    private Task? _preparation;
    private bool _closed;
    private long _revision;
    internal CanonicalAnkiReviewSession? Session => _session;
    internal AnkiReviewFeature Feature => _feature;
    internal LearningShowcaseReview(LearningShowcaseStore store, Cotabby.App.Speech.LearningSpeechController speech, VocabularyCapture capture)
    {
        _store = store; _capture = capture;
        var settings = new AppSettings();
        _feature = AnkiReviewFeature.CreateConnectionSettings(() => settings, () => { }, new SampleAnki(), new Credentials(), speech: speech, canonicalStore: store.Backend);
        Children.Add(new TextBlock { Text = "Sample Anki · Changes stay in this example", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        var sides = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (int index = 0; index < 2; index++)
        {
            int side = index; var button = new Button { Content = side == 0 ? "Front" : "Back", Margin = new Thickness(0, 0, 6, 0) };
            button.SetResourceReference(StyleProperty, "Btn"); button.Click += (_, _) => { if (_window is not null) _window.SideBox.SelectedIndex = side; }; sides.Children.Add(button);
        }
        Children.Add(sides); Children.Add(_editor); Children.Add(_message);
        var review = new Button { Content = "Review in Anki", HorizontalAlignment = HorizontalAlignment.Left };
        review.Click += async (_, _) => { try { await ShowStepAsync(3); } catch { if (!_closed) _message.Text = "The sample review could not be opened."; } };
        Children.Add(review);
    }
    private async Task PrepareAsync()
    {
        await _store.PrepareAsync();
        if (_closed) return;
        var receipt = await _store.Backend.SaveCaptureAsync(_capture);
        var destination = receipt.Destination ?? throw new InvalidOperationException("The sample capture could not be saved.");
        _session = await CanonicalAnkiReviewSession.OpenAsync(_feature.Controller, _store.Backend, destination.EntryId, destination.SenseId, Guid.NewGuid());
        if (_closed) { _session.Dispose(); return; }
        _window = new(_session, _feature.RunAsync, () => { }, embeddedExample: true);
        _editor.Content = _window.TakeExampleCard();
    }
    internal async Task ShowStepAsync(int step)
    {
        if (_closed) return;
        long revision = ++_revision;
        await (_preparation ??= PrepareAsync());
        if (_closed || revision != _revision || _session is null || _window is null) return;
        if (_window.LastOperation is { } loading) await loading;
        if (_closed || revision != _revision) return;
        if (step is 0 or 1) { _window.SideBox.SelectedIndex = step; await _window.PreviewOperation; }
        if (step < 2) return;
        await _feature.RunAsync(async token =>
        {
            if (_closed || revision != _revision || _session.Busy) return;
            if (step == 2 || _session.Binding is null)
            {
                bool sent = await _session.SendAsync(cancellationToken: token);
                if (!sent) { _message.Text = _session.Message ?? "The sample card could not be added."; return; }
            }
            if (_closed || revision != _revision || step != 3 || _session.Binding is not { } binding) return;
            var error = await _feature.Controller.ReviewCanonicalDeckAsync(binding,
                _ => Task.FromResult(!_closed && revision == _revision && Equals(binding, _session.Binding)), token);
            if (!_closed && revision == _revision) _message.Text = error ?? "Review destination: Anki · sample deck";
        });
    }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return; _closed = true; ++_revision;
        try { if (_preparation is not null) await _preparation; }
        finally
        {
            try { if (_window is not null) await _window.RetireAsync(); else _session?.Dispose(); }
            finally { await _feature.DisposeAsync(); }
        }
    }

    private sealed class SampleAnki : IAnkiConnecting
    {
        private readonly Dictionary<long, IReadOnlyDictionary<string, string>> _notes = [];
        private long _next = 1000;
        public Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters,
            AnkiConnection connection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnkiValue result;
            switch (action)
            {
                case "version": result = new AnkiValue.Number(6); break;
                case "getActiveProfile": result = new AnkiValue.Text("Lingrove sample"); break;
                case "deckNames": result = AnkiValue.List(["Default"]); break;
                case "modelNames": result = AnkiValue.List([ReviewLayoutPolicy.Model]); break;
                case "modelFieldNames": result = AnkiValue.List(["LingroveID", "Front", "Back"]); break;
                case "modelTemplates": result = new AnkiValue.Map(ReviewLayoutPolicy.Templates); break;
                case "modelStyling": result = AnkiValue.Strings(new Dictionary<string, string> { ["css"] = ReviewLayoutPolicy.Css }); break;
                case "findNotes": result = new AnkiValue.Sequence(_notes.Keys.Select(id => (AnkiValue)new AnkiValue.Number(id)).ToArray()); break;
                case "notesInfo":
                    result = new AnkiValue.Sequence(parameters["notes"].Array!.Select(value =>
                    {
                        long id = value.Integer!.Value;
                        return (AnkiValue)new AnkiValue.Map(new Dictionary<string, AnkiValue>
                        {
                            ["noteId"] = new AnkiValue.Number(id), ["modelName"] = new AnkiValue.Text(ReviewLayoutPolicy.Model),
                            ["fields"] = new AnkiValue.Map(_notes.GetValueOrDefault(id, new Dictionary<string, string>()).ToDictionary(pair => pair.Key,
                                pair => (AnkiValue)AnkiValue.Strings(new Dictionary<string, string> { ["value"] = pair.Value })))
                        });
                    }).ToArray()); break;
                case "addNote":
                    var note = parameters["note"].Object!;
                    long next = ++_next;
                    _notes[next] = note["fields"].Object!.ToDictionary(pair => pair.Key, pair => pair.Value.String!);
                    result = new AnkiValue.Number(next); break;
                case "updateNoteFields":
                    var update = parameters["note"].Object!;
                    _notes[update["id"].Integer!.Value] = update["fields"].Object!.ToDictionary(pair => pair.Key, pair => pair.Value.String!);
                    result = AnkiValue.Null; break;
                case "findCards": result = new AnkiValue.Sequence([new AnkiValue.Number(1)]); break;
                case "cardsInfo": result = new AnkiValue.Sequence([AnkiValue.Strings(new Dictionary<string, string> { ["deckName"] = "Default" })]); break;
                case "guiDeckOverview": result = new AnkiValue.Boolean(true); break;
                default: throw new InvalidOperationException("This operation is unavailable in the sample Anki destination.");
            }
            return Task.FromResult(result);
        }
    }
}
