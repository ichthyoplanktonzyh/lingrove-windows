using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;
using R = Cotabby.Core.Vocabulary.Resources;
using Live = Cotabby.Core.Vocabulary;
using L = Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Detached live-writing input; adapting it never opens an older notebook or codec.</summary>
public sealed record CanonicalWritingCapture(string Expression, string DictionaryForm, LearningLanguage? Language,
    Live.VocabularyEntryKind Kind, string Meaning, LearningLanguage? MeaningLanguage, VocabularyExample? Example,
    LearningExplanationRecord? Explanation, Guid? SenseId);
public sealed record CanonicalCaptureDraft(VocabularyDocument Document, Live.VocabularyExplanationDestination Destination);

/// <summary>Frozen writing-card save/remove and explanation rules over canonical resources.
/// The archive must hold its write span and publish only after validating and committing the draft.</summary>
public static class CanonicalWritingPolicy
{
    public static CanonicalWritingCapture FromLive(Live.VocabularyCapture capture)
    {
        Live.VocabularyCapturePolicy.Validate(capture);
        var example = capture.Example is { } value ? new VocabularyExample(value.Id, value.Text, value.Translation, value.Ranges,
            value.Completeness switch { Live.VocabularyExampleCompleteness.Sentence => R.VocabularyExampleCompleteness.Sentence,
                Live.VocabularyExampleCompleteness.Fragment => R.VocabularyExampleCompleteness.Fragment, _ => throw Invalid() }, value.Meaning,
            SourceDate.FromWindows(value.CreatedAt))
        {
            SourceKind = VocabularySourceKind.Captured, TranslationLanguage = value.TranslationLanguage,
            TranslationVariants = value.TranslationVariants, MeaningLanguage = value.MeaningLanguage,
            MeaningOrigin = value.MeaningOrigin, SourceApplicationId = value.SourceApplicationId,
            Explanations = value.Explanations.Select(FromLive).ToArray(),
        } : null;
        return new(capture.Expression, capture.DictionaryForm, capture.Language, capture.Kind, capture.Meaning, capture.MeaningLanguage,
            example, capture.Explanation is { } explanation ? FromLive(explanation) : null, capture.SenseId);
    }
    public static R.LearningExplanationInput FromLive(L.LearningExplanationInput value) => new(value.Target, value.DictionaryForm,
        value.Language, value.Kind, value.Context, value.Ranges, value.ContextKind switch
        { L.LearningExplanationContext.Sentence => R.LearningExplanationContext.Sentence, L.LearningExplanationContext.Fragment => R.LearningExplanationContext.Fragment,
            L.LearningExplanationContext.None => R.LearningExplanationContext.None, L.LearningExplanationContext.Unknown => R.LearningExplanationContext.Unknown, _ => throw Invalid() }, value.OccurrenceIdentity);
    public static LearningExplanationRecord FromLive(L.LearningExplanationRecord value) => new(value.Id, FromLive(value.Input), value.Language,
        value.Result, value.Model, value.CanonicalCreatedAt is { } source && source.TryToWindows(out var projected, out _) && projected == value.CreatedAt
            ? source : SourceDate.FromWindows(value.CreatedAt));
    public static L.LearningExplanationRecord ToLive(LearningExplanationRecord value)
    {
        if (!value.CreatedAt.TryToWindows(out var created, out _)) throw Invalid();
        var language = value.Input.Language;
        var input = new L.LearningExplanationInput(value.Input.Target, value.Input.DictionaryForm, language, value.Input.Kind,
            value.Input.Context, value.Input.Ranges, value.Input.ContextKind switch
            { R.LearningExplanationContext.Sentence => L.LearningExplanationContext.Sentence, R.LearningExplanationContext.Fragment => L.LearningExplanationContext.Fragment,
                R.LearningExplanationContext.None => L.LearningExplanationContext.None, R.LearningExplanationContext.Unknown => L.LearningExplanationContext.Unknown, _ => throw Invalid() }, value.Input.OccurrenceIdentity);
        return new(value.Id, input, value.Language, value.Result, value.Model, created) { CanonicalCreatedAt = value.CreatedAt };
    }
    private static string EntryKey(CanonicalWritingCapture capture) => VocabularyArchiveValidation.EntryKey(new VocabularyEntry(Guid.Empty,
        capture.Expression, capture.DictionaryForm, capture.Language, capture.Kind, [], new SourceDate(0), new SourceDate(0)));
    private static string Key(VocabularyExample example)
    {
        string ranges = string.Join(",", example.Ranges.Select(x => x.Location.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + x.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        string kind = example.Completeness switch { R.VocabularyExampleCompleteness.Sentence => "sentence", R.VocabularyExampleCompleteness.Fragment => "fragment",
            R.VocabularyExampleCompleteness.Unknown => "unknown", _ => throw Invalid() };
        var parts = new[] { example.Text, ranges, kind };
        string framed = string.Concat(parts.Select(x => System.Text.Encoding.UTF8.GetByteCount(x).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + x));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(framed)));
    }
    private static int? Index(Guid id, VocabularyEntry entry)
    { for (int i = 0; i < entry.Senses.Count; i++) if (entry.Senses[i].Id == id || entry.Senses[i].MergedFrom.Contains(id)) return i; return null; }
    private static List<Live.VocabularyExplanationDestination> Destinations(CanonicalWritingCapture capture, VocabularyEntry entry)
    {
        var exact = new List<Live.VocabularyExplanationDestination>(); var compatible = new List<Live.VocabularyExplanationDestination>();
        foreach (var sense in entry.Senses)
        {
            if (capture.SenseId is { } id && sense.Id != id && !sense.MergedFrom.Contains(id)) continue;
            if (capture.Example is not { } incoming)
            { if (sense.IsSavedWithoutContext) exact.Add(new(entry.Id, sense.Id, null)); continue; }
            foreach (var existing in sense.Examples)
            {
                var destination = new Live.VocabularyExplanationDestination(entry.Id, sense.Id, existing.Id);
                if (Key(existing) == Key(incoming)) exact.Add(destination);
                else if ((existing.Completeness == R.VocabularyExampleCompleteness.Unknown || incoming.Completeness == R.VocabularyExampleCompleteness.Unknown)
                    && StringComparer.Ordinal.Equals(existing.Text, incoming.Text) && CollectionValues.SequenceEqual(existing.Ranges, incoming.Ranges)) compatible.Add(destination);
            }
        }
        return exact.Count == 0 ? compatible : exact;
    }
    public static Live.VocabularyExplanationDestination? Destination(CanonicalWritingCapture capture, VocabularyEntry entry)
    { var values = Destinations(capture, entry); return values.Count == 1 ? values[0] : null; }
    public static Live.VocabularyExplanationDestination? SavedDestination(Live.VocabularyCapture capture, VocabularyDocument document)
    {
        var adapted = FromLive(capture);
        var entry = document.CollectedEntries.FirstOrDefault(x => VocabularyArchiveValidation.EntryKey(x) == EntryKey(adapted));
        return entry is null ? null : Destination(adapted, entry);
    }
    public static L.LearningExplanationRecord? FindExplanation(L.LearningExplanationInput input, LearningLanguage language, VocabularyDocument document)
    {
        var expected = FromLive(input);
        var record = document.CollectedEntries.SelectMany(x => x.Explanations ?? []).LastOrDefault(x => x.Input == expected && x.Language == language);
        return record is null ? null : ToLive(record);
    }
    private static VocabularyExample AddTranslation(VocabularyExample example, string text, LearningLanguage? language)
    {
        if (text.Length == 0) return example;
        var value = new Live.VocabularyMeaning(text, language);
        if (!example.TranslationVariants.Contains(value)) example = example with { TranslationVariants = example.TranslationVariants.Append(value).ToArray() };
        return example.Translation.Length == 0 ? example with { Translation = text, TranslationLanguage = language } : example;
    }
    public static CanonicalCaptureDraft Save(CanonicalWritingCapture capture, VocabularyDocument document, SourceDate now)
    {
        var entries = document.Entries.ToList(); string key = EntryKey(capture); var example = capture.Example;
        if (example is not null && entries.Any(entry => VocabularyArchiveValidation.EntryKey(entry) != key && entry.Examples.Any(x => x.Id == example.Id)))
            example = example.CopiedWithNewId();
        capture = capture with { Example = example };
        int index = entries.FindIndex(entry => VocabularyArchiveValidation.EntryKey(entry) == key);
        if (index < 0)
        {
            if (capture.SenseId is not null) throw Invalid();
            entries.Add(new VocabularyEntry(Guid.NewGuid(), capture.Expression, capture.DictionaryForm, capture.Language, capture.Kind, [], now, now)); index = entries.Count - 1;
        }
        var entry = entries[index]; var matches = Destinations(capture, entry);
        if (capture.Example is not null && matches.Count > 1) throw Conflict();
        var prior = matches.Count == 1 ? matches[0] : (Live.VocabularyExplanationDestination?)null;
        int senseIndex;
        var senses = entry.Senses.ToList();
        if (capture.SenseId is { } named) senseIndex = Index(named, entry) ?? throw Invalid();
        else if (prior is { } destination) senseIndex = Index(destination.SenseId, entry) ?? throw Invalid();
        else { senses.Add(new VocabularySense(Guid.NewGuid(), capture.Example is null ? capture.Meaning : "", now, now)
            { MeaningLanguage = capture.Example is null ? capture.MeaningLanguage : null }); senseIndex = senses.Count - 1; }
        var sense = senses[senseIndex];
        if (example is not null)
        {
            if (senses.Where((_, i) => i != senseIndex).Any(other => other.Examples.Any(x => Key(x) == Key(example)))) throw Conflict();
            var examples = sense.Examples.ToList(); int existing = examples.FindIndex(x => x.Id == prior?.ExampleId || Key(x) == Key(example));
            if (existing >= 0)
            {
                var saved = examples[existing];
                if (saved.Completeness == R.VocabularyExampleCompleteness.Unknown) saved = saved with { Completeness = example.Completeness };
                examples[existing] = AddTranslation(saved, example.Translation, example.TranslationLanguage);
            }
            else
            {
                if (entry.Examples.Any(x => x.Id == example.Id)) example = example.CopiedWithNewId();
                examples.Add(AddTranslation(example, example.Translation, example.TranslationLanguage));
            }
            sense = sense with { Examples = examples };
        }
        else
        {
            if (senses.Where((_, i) => i != senseIndex).Any(other => other.IsSavedWithoutContext)) throw Conflict();
            var meaning = new Live.VocabularyMeaning(capture.Meaning, capture.MeaningLanguage);
            sense = sense with { IsSavedWithoutContext = true, StandaloneMeanings = sense.StandaloneMeanings.Contains(meaning)
                ? sense.StandaloneMeanings : sense.StandaloneMeanings.Append(meaning).ToArray() };
        }
        senses[senseIndex] = sense with { UpdatedAt = now }; entry = entry with { Senses = senses, UpdatedAt = now };
        if (capture.Explanation is { } explanation) entry = StoreExplanation(explanation, entry);
        entries[index] = entry; var next = document.WithEntries(entries);
        var resolved = Destination(capture, entry) ?? throw Conflict();
        next = VocabularyNotebookProjection.Collect(resolved.SenseId, resolved.ExampleId, next, now, capture.Example is null);
        return new(next, resolved);
    }
    public static VocabularyDocument? Remove(CanonicalWritingCapture capture, VocabularyDocument document)
    {
        var entry = document.CollectedEntries.FirstOrDefault(x => VocabularyArchiveValidation.EntryKey(x) == EntryKey(capture));
        if (entry is null || Destination(capture, entry) is not { } destination) return null;
        var collections = document.Collections.ToList(); int index = collections.FindIndex(x => x.SenseId == destination.SenseId);
        if (index < 0) return null;
        var current = collections[index];
        var next = destination.ExampleId is { } usage ? current with { InterpretationIds = current.InterpretationIds.Where(x => x != usage).ToArray() }
            : current with { IsSavedWithoutContext = false };
        if (next.InterpretationIds.Count == 0 && !next.IsSavedWithoutContext && next.Note.Length == 0 && next.Tags.Count == 0
            && (next.Content?.Count ?? 0) == 0 && next.Review is null && (next.ReviewCards?.Count ?? 0) == 0) collections.RemoveAt(index);
        else collections[index] = next;
        return document with { Collections = collections };
    }
    internal static R.LearningExplanationInput Input(VocabularyEntry entry, VocabularySense sense, VocabularyExample? example) =>
        new(entry.Expression, entry.DictionaryForm, entry.Language, entry.Kind, example?.Text ?? entry.Expression,
            example?.Ranges ?? [new Live.VocabularyTextRange(0, entry.Expression.Length)], example is null ? R.LearningExplanationContext.None
                : example.Completeness switch { R.VocabularyExampleCompleteness.Sentence => R.LearningExplanationContext.Sentence,
                    R.VocabularyExampleCompleteness.Fragment => R.LearningExplanationContext.Fragment, _ => R.LearningExplanationContext.Unknown },
            example is not null ? Key(example) : sense.CreatedWithoutContext == true && !sense.IsSavedWithoutContext ? "sense:" + sense.Id.ToString("D").ToUpperInvariant() : null);
    private static IReadOnlyList<LearningExplanationRecord> Append(LearningExplanationRecord value, IReadOnlyList<LearningExplanationRecord> records) =>
        records.Where(x => x.Input != value.Input || x.Language != value.Language).Append(value).TakeLast(24).ToArray();
    public static bool Accepts(LearningExplanationRecord value, VocabularyEntry entry) => entry.Senses.Any(sense =>
        sense.SupportsGeneralExplanation && Input(entry, sense, null) == value.Input || sense.Examples.Any(example => Input(entry, sense, example) == value.Input));
    public static VocabularyEntry StoreExplanation(LearningExplanationRecord value, VocabularyEntry entry)
    {
        if (!Enum.IsDefined(value.Language) || !Enum.IsDefined(value.Input.Kind) || !Enum.IsDefined(value.Input.ContextKind)
            || value.Input.Language is { } language && !Enum.IsDefined(language)
            || !VocabularyArchiveValidation.ValidInput(value.Input) || !L.LearningExplanationPolicy.IsValid(value.Result)) return entry;
        var senses = entry.Senses.ToArray();
        for (int i = 0; i < senses.Length; i++)
        {
            var sense = senses[i];
            if (sense.SupportsGeneralExplanation && Input(entry, sense, null) == value.Input)
            {
                sense = sense with { Explanations = Append(value, sense.Explanations) };
                if (sense.Meaning.Length == 0 && sense.MeaningOrigin != Live.VocabularyMeaningOrigin.User)
                    sense = sense with { Meaning = value.Result.Meaning, MeaningLanguage = value.Language, MeaningOrigin = Live.VocabularyMeaningOrigin.Ai };
            }
            var examples = sense.Examples.ToArray();
            for (int j = 0; j < examples.Length; j++) if (Input(entry, sense, examples[j]) == value.Input)
            {
                examples[j] = examples[j] with { Explanations = Append(value, examples[j].Explanations) };
                if (examples[j].MeaningOrigin != Live.VocabularyMeaningOrigin.User)
                    examples[j] = examples[j] with { Meaning = value.Result.Meaning, MeaningLanguage = value.Language, MeaningOrigin = Live.VocabularyMeaningOrigin.Ai };
            }
            senses[i] = sense with { Examples = examples };
        }
        return entry with { Senses = senses };
    }
    public static VocabularyDocument? SaveExplanation(LearningExplanationRecord value, VocabularyDocument document)
    {
        var entries = document.Entries.ToArray(); int index = Array.FindIndex(entries, x => Accepts(value, x));
        if (index < 0 || !L.LearningExplanationPolicy.IsValid(value.Result)) return null;
        entries[index] = StoreExplanation(value, entries[index]); return document.WithEntries(entries);
    }
    public static VocabularyDocument? Adopt(LearningExplanationRecord value, Live.VocabularyExplanationDestination destination, VocabularyDocument document)
    {
        if (!L.LearningExplanationPolicy.IsValid(value.Result)) return null;
        // The UI destination is a personal collection. A late response cannot adopt into a
        // removed membership even though the underlying canonical evidence is deliberately kept.
        var visible = document.CollectedEntries.FirstOrDefault(x => x.Id == destination.EntryId);
        if (visible is null) return null;
        if (destination.ExampleId is { } visibleUsage)
        {
            if (!visible.Examples.Any(x => x.Id == visibleUsage)) return null;
        }
        else if (Index(destination.SenseId, visible) is null) return null;
        var entries = document.Entries.ToArray(); int index = Array.FindIndex(entries, x => x.Id == destination.EntryId); if (index < 0) return null;
        var entry = entries[index]; var senses = entry.Senses.ToArray();
        if (destination.ExampleId is { } usage)
        {
            int owner = Array.FindIndex(senses, x => x.Examples.Any(e => e.Id == usage)); if (owner < 0) return null;
            var examples = senses[owner].Examples.ToArray(); int i = Array.FindIndex(examples, x => x.Id == usage);
            if (Input(entry, senses[owner], examples[i]) != value.Input) return null;
            examples[i] = examples[i] with { Meaning = value.Result.Meaning, MeaningLanguage = value.Language, MeaningOrigin = Live.VocabularyMeaningOrigin.User };
            senses[owner] = senses[owner] with { Examples = examples };
        }
        else
        {
            int? owner = Index(destination.SenseId, entry); if (owner is null || !senses[owner.Value].SupportsGeneralExplanation
                || Input(entry, senses[owner.Value], null) != value.Input) return null;
            senses[owner.Value] = senses[owner.Value] with { Meaning = value.Result.Meaning, MeaningLanguage = value.Language, MeaningOrigin = Live.VocabularyMeaningOrigin.User };
        }
        entries[index] = entry with { Senses = senses }; return document.WithEntries(entries);
    }
    /// <summary>Frozen archive commit preparation: validate raw relationships before joined
    /// projection, invalidate changed semantic evidence, and prune explanations for old inputs.</summary>
    public static VocabularyDocument PrepareCommit(VocabularyDocument previous, VocabularyDocument next) => PrepareCommitCore(previous, next, false);
    /// <summary>A validated backup replaces evidence and assertions together. Do not reconcile it
    /// against today's graph; still prune explanations whose exact restored inputs no longer match.</summary>
    public static VocabularyDocument PrepareRestore(VocabularyDocument next) => PrepareCommitCore(next, next, true);
    private static VocabularyDocument PrepareCommitCore(VocabularyDocument previous, VocabularyDocument next, bool restoringBackup)
    {
        VocabularyLifecyclePolicy.Validate(next);
        var entries = next.Entries.ToArray();
        if (!restoringBackup)
        {
            var oldEntries = previous.Entries;
            var oldSenses = oldEntries.SelectMany(x => x.Senses).Select(x => x.Id).ToHashSet();
            var senses = entries.SelectMany(x => x.Senses).ToArray(); var live = senses.Select(x => x.Id).ToHashSet();
            var aliases = new Dictionary<Guid, Guid>();
            foreach (var sense in senses) foreach (var alias in sense.MergedFrom) aliases[alias] = sense.Id;
            Guid? Resolve(Guid id) => live.Contains(id) ? id : aliases.TryGetValue(id, out var owner) ? owner : oldSenses.Contains(id) ? null : id;
            var patterns = new List<VocabularyUsagePattern>();
            foreach (var value in next.Semantics.Patterns) if (Resolve(value.SenseId) is { } id) patterns.Add(value with { SenseId = id });
            var removedPatterns = previous.Semantics.Patterns.Select(x => x.Id).Except(patterns.Select(x => x.Id)).ToHashSet();
            var oldExamples = oldEntries.SelectMany(x => x.Examples).ToDictionary(x => x.Id);
            var changedEvidence = new HashSet<Guid>();
            foreach (var example in senses.SelectMany(x => x.Examples))
                if (oldExamples.TryGetValue(example.Id, out var before) && (before.SourceId != example.SourceId || before.OccurrenceId != example.OccurrenceId
                    || !CollectionValues.Same(before.Text, example.Text) || !CollectionValues.SequenceEqual(before.Ranges, example.Ranges)
                    || !CollectionValues.Same(before.Meaning, example.Meaning) || before.MeaningLanguage != example.MeaningLanguage)) changedEvidence.Add(example.Id);
            var evidenceBySense = senses.ToDictionary(x => x.Id, x => x.Examples.Select(e => e.Id).ToHashSet());
            var mappings = new List<VocabularyMeaningMapping>();
            foreach (var value in next.Semantics.Mappings)
            {
                if (Resolve(value.SenseId) is not { } senseId || value.PatternId is { } pattern && removedPatterns.Contains(pattern)) continue;
                var valid = evidenceBySense.GetValueOrDefault(senseId) ?? [];
                var evidence = value.EvidenceIds.Where(id => !changedEvidence.Contains(id) && (!oldExamples.ContainsKey(id) || valid.Contains(id))).ToArray();
                var mapping = value with { SenseId = senseId, EvidenceIds = evidence };
                if (!CollectionValues.SequenceEqual(evidence, value.EvidenceIds)) mapping = mapping with { Provenance = mapping.Provenance with { Status = VocabularySemanticStatus.Proposed } };
                mappings.Add(mapping);
            }
            next = next with { Semantics = next.Semantics with { Patterns = patterns, Mappings = mappings } };
        }
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var entrySenses = entry.Senses.ToArray();
            for (int j = 0; j < entrySenses.Length; j++)
            {
                var sense = entrySenses[j]; var general = Input(entry, sense, null);
                var examples = sense.Examples.Select(example => example with
                { Explanations = example.Explanations.Where(record => record.Input == Input(entry, sense, example)).ToArray() }).ToArray();
                entrySenses[j] = sense with { Explanations = sense.Explanations.Where(record => record.Input == general).ToArray(), Examples = examples };
            }
            entries[i] = entry with { Senses = entrySenses };
        }
        return next.WithEntries(entries);
    }
    private static Live.VocabularyException Invalid() => new(Live.VocabularyFailure.InvalidContent);
    private static Live.VocabularyException Conflict() => new(Live.VocabularyFailure.EditConflict);
}
