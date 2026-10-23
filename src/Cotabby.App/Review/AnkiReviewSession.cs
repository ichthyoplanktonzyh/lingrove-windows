using System.Collections.ObjectModel;
using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>One UI-context preparation draft borrows the shared controller/Store. This part owns
/// editable presentation state and acknowledged baseline; metadata, sending and media partials
/// retain their separate sequencing responsibilities. Awaiting calls preserve the caller's UI
/// context. A window owns cancellation and closes its audio preview when releasing the session.</summary>
internal sealed partial class AnkiReviewSession : INotifyPropertyChanged
{
    internal AnkiReviewController Controller { get; }
    internal Guid EntryId { get; }
    internal Guid SenseId { get; }
    internal Guid? CardId { get; }
    internal ReviewAudioPreparation Audio { get; }
    private IReadOnlyDictionary<string, VocabularyPreparedImage> _importedImages = new ReadOnlyDictionary<string, VocabularyPreparedImage>(new Dictionary<string, VocabularyPreparedImage>());
    internal IReadOnlyDictionary<string, VocabularyPreparedImage> ImportedImages
    {
        get => _importedImages;
        set { _importedImages = new ReadOnlyDictionary<string, VocabularyPreparedImage>(new Dictionary<string, VocabularyPreparedImage>(value, CanonicalStringComparer.Instance)); Changed(); }
    }
    private bool _importingImage;
    internal bool ImportingImage { get => _importingImage; set { _importingImage = value; Changed(); } }
    internal bool CurrentPresetTemplate { get; private set; }
    internal bool SafeGapFront { get; private set; }
    internal bool LegacyPresetTemplate { get; private set; }
    internal string PresetCss { get; private set; } = AnkiNoteTemplate.Css;
    private ReviewPlan _plan;
    internal ReviewPlan Plan
    {
        get => _plan;
        set
        {
            if (value.Layout is { } layout && (value.AudioKinds is null || !value.AudioKinds.SetEquals(layout.AudioKinds)))
                value = value with { AudioKinds = layout.AudioKinds };
            _plan = value; Changed();
        }
    }
    private Guid? _bindingId;
    internal Guid? BindingId => _bindingId;
    private string _deck;
    internal string Deck { get => _deck; set { _deck = value; Changed(); } }
    internal IReadOnlyList<AnkiFieldConflict> Conflicts { get; private set; } = [];
    private readonly Dictionary<string, bool> _keepRemote = new(CanonicalStringComparer.Instance);
    internal IReadOnlyDictionary<string, bool> KeepRemote => new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(_keepRemote, CanonicalStringComparer.Instance));
    internal void SetKeepRemote(string field, bool value) { _keepRemote[field] = value; Changed(); }
    internal string? Message { get; private set; }
    internal bool Busy { get; private set; }
    internal bool MissingNote { get; private set; }
    internal AnkiReviewCatalog? Catalog { get; private set; }
    internal IReadOnlyList<string> AvailableFields { get; private set; } = [];
    internal bool Loading { get; private set; }
    private VocabularyReview? _baseline;
    private AnkiRemoteNote? _conflictRemote;
    private Guid _metadataId = Guid.NewGuid();
    private ReviewOperationScope? _sendScope;
    private readonly string _initialDefaultDeck;
    private readonly bool _initialDefaultRecognition, _initialDefaultGap;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    internal AnkiReviewSession(AnkiReviewController controller, VocabularyEntry entry, VocabularySense sense,
        ReviewOperationScope? defaults = null, Guid? cardId = null)
    {
        Controller = controller; EntryId = entry.Id; SenseId = sense.Id; CardId = cardId;
        _initialDefaultDeck = defaults?.DefaultDeck ?? controller.Settings.Deck;
        _initialDefaultRecognition = defaults?.DefaultRecognition ?? controller.Settings.DefaultRecognition;
        _initialDefaultGap = defaults?.DefaultGap ?? controller.Settings.DefaultGap;
        Audio = new(controller.Speech); _baseline = sense.ReviewValue(cardId);
        var initial = _baseline?.Plan ?? ReviewCardPolicy.InitialPlan(sense);
        if (cardId is { } id && _baseline is null)
            initial = new ReviewPlan(id) { Name = "New card", Layout = ReviewLayoutPolicy.Preset(0), ExampleId = sense.Examples.FirstOrDefault()?.Id };
        if (_baseline is null && cardId is null)
        {
            var example = sense.Examples.FirstOrDefault();
            bool gap = _initialDefaultGap && example is not null && ReviewCardPolicy.Gap(example.Text, example.Ranges, entry.Kind) is not null;
            initial = initial with { Recognition = _initialDefaultRecognition || !gap, Gap = gap };
        }
        // Initial assignment bypasses didSet. The later wrapped-property member mutation for
        // non-preset models DOES invoke it in the original @Published owner (measured natively),
        // so only that final recognition edit synchronizes layout/audio here.
        _plan = initial; _bindingId = _baseline?.ActiveBindingId;
        _deck = _baseline?.ActiveBinding?.Deck ?? initial.Deck ?? _initialDefaultDeck;
        if (!Same(Model, AnkiReviewPolicy.PresetName)) Plan = _plan with { Recognition = true };
        // Audio belongs to this session; CLR collects this local owner/event cycle together.
        // There is no subscription on a process-wide provider retaining a closed window.
        Audio.PropertyChanged += (_, _) => Changed();
    }
    internal (VocabularyEntry Entry, VocabularySense Sense)? Current
    { get { try { return Controller.Current(EntryId, SenseId); } catch { return null; } } }
    internal AnkiNoteBinding? Binding => _baseline?.Bindings.FirstOrDefault(b => b.Id == _bindingId);
    internal string Model => Binding?.Model ?? (CardId is null ? Controller.Settings.Model : ReviewLayoutPolicy.Model);
    internal VocabularyReview? SavedReview => Current?.Sense.ReviewValue(CardId);
    internal IReadOnlyDictionary<ReviewField, string> Mapping
    {
        get
        {
            if (CardId is not null) return ReviewLayoutPolicy.Mapping;
            if (Same(Model, AnkiReviewPolicy.PresetName))
                return new ReadOnlyDictionary<ReviewField, string>(AnkiReviewPolicy.PresetMapping.Where(p =>
                    p.Key is not (ReviewField.ExpressionAudio or ReviewField.SentenceAudio) || AvailableFields.Count == 0 || Has(AvailableFields, FieldName(p.Key))).ToDictionary(p => p.Key, p => p.Value));
            return Binding?.Mapping ?? Controller.Settings.EffectiveMapping;
        }
    }
    internal ReviewCardContent? Content
    { get { try { return Current is { } c ? ReviewCardPolicy.Content(c.Entry, c.Sense, Plan) : null; } catch { return null; } } }
    internal string? ValidationMessage
    {
        get
        {
            if (Current is not { } current) return Conflict().Message;
            try
            {
                if (Binding is { } binding && AnkiReviewPolicy.RemovesExistingCard(Plan, binding)) return "Manage existing card types in Anki.";
                if (_baseline?.NeedsBindingChoice == true && BindingId is null || Deck.Length == 0
                    || Binding?.NoteId is null && Catalog is { } catalog && !Has(catalog.Decks, Deck)) return "Choose a deck and an Anki association.";
                var content = ReviewCardPolicy.Content(current.Entry, current.Sense, Plan);
                if (Same(Model, AnkiReviewPolicy.PresetName) && Plan.Gap && content.SenseContent.Count > 0 && !SafeGapFront && !Loading)
                    return "Update the Lingrove template before adding explanation content to gap cards.";
                ValidateAudioMapping();
                var fields = AnkiReviewPolicy.Fields(content, Binding?.OriginSenseId ?? CardId ?? SenseId, Mapping, Plan.FieldOverrides);
                return AvailableFields.Count > 0 && fields.Keys.Any(k => !Has(AvailableFields, k)) ? "The Anki note type does not contain the mapped fields." : null;
            }
            catch (Exception error) { return error.Message; }
        }
    }
    internal string Answer
    {
        get
        {
            if (Plan.Answer is { } answer) return answer;
            if (Current is not { } c) return "";
            try { return ReviewCardPolicy.Content(c.Entry, c.Sense, Plan with { Recognition = true, Gap = false }).Answer; } catch { return ""; }
        }
    }
    internal bool CanGap => Current is { } c && c.Sense.Examples.FirstOrDefault(e => e.Id == Plan.ExampleId) is { } example
        && ReviewCardPolicy.Gap(example.Text, example.Ranges, c.Entry.Kind) is not null;
    internal bool HasExistingCard(ReviewField field) => Binding is { NoteId: not null } b && b.Mapping.TryGetValue(field, out string? name) && (b.Fields.GetValueOrDefault(name) ?? "").Length > 0;
    internal void SelectBinding(Guid? id)
    {
        _bindingId = id;
        if (Binding is { } b) Deck = b.Deck;
        if (!Same(Model, AnkiReviewPolicy.PresetName)) Plan = Plan with { Recognition = true };
        Conflicts = []; _keepRemote.Clear(); _conflictRemote = null; Changed();
    }
    internal void RestorePlan(ReviewPlan saved)
    {
        var restored = saved with { Id = Guid.NewGuid() };
        if (!Same(Model, AnkiReviewPolicy.PresetName)) restored = restored with { Recognition = true };
        Plan = restored;
        if (Binding?.NoteId is null && saved.Deck is { } deck) Deck = deck;
    }
    internal async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        try { await SavePlanAsync(cancellationToken); return true; }
        catch (Exception error) { Message = error.Message; Changed(); return false; }
    }
    private async Task SavePlanAsync(CancellationToken cancellationToken)
    {
        var current = Controller.Current(EntryId, SenseId);
        if (!Equals(current.Sense.ReviewValue(CardId), _baseline)) throw Conflict();
        if (CardId is not null && (Plan.Name is null || LearningTrim(Plan.Name).Length == 0)) Plan = Plan with { Name = "New card" };
        Plan = Plan with { Deck = Deck };
        var next = (_baseline ?? new VocabularyReview(Plan)) with { Plan = Plan, ActiveBindingId = BindingId };
        int index = next.Bindings.ToList().FindIndex(b => b.Id == BindingId && b.NoteId is null);
        if (index >= 0) { var bindings = next.Bindings.ToArray(); bindings[index] = bindings[index] with { Deck = Deck }; next = next with { Bindings = bindings }; }
        // Swift's task cancellation follows the Store call into Archive+Media, which checks it
        // before the atomic SQL commit, even when there are no image references. Preserve that
        // boundary through the explicit token; cancellation never undoes an acknowledged commit
        // or deletes remote media that an already-committed Anki note might reference.
        await Controller.Store.SaveReviewAsync(EntryId, SenseId, next, _baseline, CardId, ImportedImages, cancellationToken);
        _baseline = next; ImportedImages = new Dictionary<string, VocabularyPreparedImage>();
    }
    private static string LearningTrim(string text) => FoundationText.TrimWhitespaceAndNewlines(text);
    private static bool Same(string? a, string? b) => CanonicalStringComparer.Instance.Equals(a, b);
    private static bool Has(IEnumerable<string> values, string value) => values.Contains(value, CanonicalStringComparer.Instance);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
    private static string FieldName(ReviewField field) => System.Text.Json.JsonSerializer.SerializeToElement(field).GetString()!;
    private static IReadOnlyDictionary<string, AnkiValue> Params(params (string Key, AnkiValue Value)[] values) => new ReadOnlyDictionary<string, AnkiValue>(values.ToDictionary(v => v.Key, v => v.Value, CanonicalStringComparer.Instance));
}
