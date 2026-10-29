using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests;

/// <summary>Current Mac lifecycle/projection regressions exercise independent resources rather
/// than the old entry-owned archive. These tests do not claim SQLite/backup or UI activation.</summary>
public sealed class VocabularyResourceLifecycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:x12}");
    private static R.VocabularyDocument Evidence()
    {
        var source = new R.VocabularySourceSnapshot(Id(1), "The bank is closed.", R.VocabularySourceKind.Imported, null, Now);
        return new() { Sources = [source], Occurrences = [new(Id(2), source.Id, [new(4, 4)])] };
    }
    private static R.VocabularyDocument Knowledge()
    {
        var sense = new R.VocabularySense(Id(3), "银行", Now, Now) { CreatedWithoutContext = true };
        return Evidence() with
        {
            Catalog = [new(Id(4), "bank", "bank", null, VocabularyEntryKind.Word, [sense], Now, Now)],
            Interpretations = [new R.VocabularyInterpretation(Id(5), Id(2), Now)
                { SenseId = sense.Id, Status = R.VocabularyInterpretationStatus.Accepted, Meaning = "银行" },
                new(Id(6), Id(2), Now) { Meaning = "河岸" }],
            Semantics = new() { Nodes = [new(Id(7), [new("金融机构") { Id = Id(8), Language = LearningLanguage.Chinese }])],
                Mappings = [new(Id(7), sense.Id) { Id = Id(9), EvidenceIds = [Id(5)] }] },
        };
    }
    private static void Reject(R.VocabularyDocument document) => Assert.Equal(VocabularyFailure.InvalidArchive,
        Assert.Throws<VocabularyException>(() => R.VocabularyLifecyclePolicy.Validate(document)).Failure);

    [Fact]
    public void EvidenceAndCompetingClaimsNeedNeitherLexicalNorCollectionOwnership()
    {
        var evidence = Evidence(); R.VocabularyLifecyclePolicy.Validate(evidence);
        var unresolved = evidence with { Interpretations = [new(Id(5), Id(2), Now) { Meaning = "银行" }, new(Id(6), Id(2), Now) { Meaning = "河岸" }] };
        R.VocabularyLifecyclePolicy.Validate(unresolved); Assert.Empty(unresolved.Entries); Assert.Empty(unresolved.CollectedEntries);
        Assert.All(unresolved.Interpretations, claim => { Assert.Null(claim.SenseId); Assert.Equal(R.VocabularyExampleCompleteness.Unknown, claim.Completeness); });
        var emptyLexical = unresolved with { Catalog = [new(Id(4), "bank", "bank", null, VocabularyEntryKind.Word, [], Now, Now)] };
        R.VocabularyLifecyclePolicy.Validate(emptyLexical); Assert.Null(Assert.Single(emptyLexical.Entries).Language);
        Assert.Empty(emptyLexical.CollectedEntries); Assert.Equal(7, emptyLexical.SchemaVersion);
    }

    [Fact]
    public void CollectionSelectsAcceptedExamplesAndCanBeRemovedAndRestoredWithoutDeletingKnowledge()
    {
        var knowledge = Knowledge(); R.VocabularyLifecyclePolicy.Validate(knowledge);
        Assert.Single(knowledge.Entries[0].Examples); Assert.Empty(knowledge.CollectedEntries);
        var collected = R.VocabularyNotebookProjection.Collect(Id(3), Id(5), knowledge, Now);
        collected = R.VocabularyNotebookProjection.Collect(Id(3), Id(5), collected, Now.AddHours(1));
        R.VocabularyLifecyclePolicy.Validate(collected);
        Assert.Equal(Now, Assert.Single(collected.Collections).SavedAt); Assert.Single(collected.Collections[0].InterpretationIds);
        var example = Assert.Single(Assert.Single(collected.CollectedEntries).Examples);
        Assert.Equal(Id(5), example.Id); Assert.Equal(Id(1), example.SourceId); Assert.Equal(Id(2), example.OccurrenceId);
        Assert.True(collected.CollectedEntries[0].Senses[0].SupportsGeneralExplanation); Assert.False(collected.Collections[0].IsSavedWithoutContext);
        var removed = collected with { Collections = [] };
        R.VocabularyLifecyclePolicy.Validate(removed); Assert.Empty(removed.CollectedEntries);
        Assert.Equal(knowledge.Catalog, removed.Catalog); Assert.Equal(knowledge.Interpretations, removed.Interpretations);
        Assert.Equal(knowledge.Sources, removed.Sources); Assert.Equal(knowledge.Occurrences, removed.Occurrences); Assert.Equal(knowledge.Semantics, removed.Semantics);
        var restored = R.VocabularyNotebookProjection.Collect(Id(3), example.Id, removed, Now);
        Assert.Equal(example.Id, Assert.Single(restored.CollectedEntries[0].Examples).Id); Assert.Equal(2, restored.Interpretations.Count);
        var noExamples = R.VocabularyNotebookProjection.Collect(Id(3), null, knowledge, Now);
        Assert.Empty(noExamples.CollectedEntries[0].Examples); Assert.Single(noExamples.Entries[0].Examples);
    }

    [Fact]
    public void JoinedEditsSeparatePersonalStateAndForkCorrectedEvidenceWithoutChangingOtherClaims()
    {
        var knowledge = R.VocabularyNotebookProjection.Collect(Id(3), Id(5), Knowledge(), Now);
        var provenance = new R.VocabularySemanticProvenance { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed, Confidence = .7 };
        knowledge = knowledge with { Interpretations = [knowledge.Interpretations[0] with { Provenance = provenance }, knowledge.Interpretations[1]] };
        var entry = knowledge.Entries[0]; var sense = entry.Senses[0]; var original = sense.Examples[0];
        var edited = entry with { Senses = [sense with { Note = "personal note", Tags = ["finance"], Examples = [original with { Text = "The bank is open." }] }] };
        var changed = knowledge.WithEntries([edited]); R.VocabularyLifecyclePolicy.Validate(changed);
        Assert.Equal(2, changed.Sources.Count); Assert.Equal(2, changed.Occurrences.Count);
        Assert.Equal(knowledge.Sources[0], changed.Sources[0]); Assert.Equal(knowledge.Interpretations[1], changed.Interpretations[1]);
        Assert.Equal(original.Id, changed.Interpretations[0].Id); Assert.NotEqual(knowledge.Interpretations[0].OccurrenceId, changed.Interpretations[0].OccurrenceId);
        Assert.Equal(provenance, changed.Interpretations[0].Provenance); Assert.Equal("personal note", Assert.Single(changed.Collections).Note);
        Assert.Empty(changed.Catalog[0].Senses[0].Examples); Assert.Empty(changed.Catalog[0].Senses[0].Note); Assert.Empty(changed.Catalog[0].Senses[0].Tags);
        Assert.Equal("The bank is open.", changed.CollectedEntries[0].Examples[0].Text); Assert.Equal(knowledge.Semantics, changed.Semantics);
        var translationOnly = knowledge.WithEntries([entry with { Senses = [sense with { Examples = [original with { Translation = "银行关门了。" }] }] }]);
        Assert.Single(translationOnly.Sources); Assert.Single(translationOnly.Occurrences); Assert.Equal(Id(2), translationOnly.Interpretations[0].OccurrenceId);
    }

    [Fact]
    public void LexicalDeletionDetachesUnresolvedClaimsButRetainsEvidence()
    {
        var knowledge = Knowledge();
        knowledge = knowledge with { Interpretations = [knowledge.Interpretations[0], knowledge.Interpretations[1] with
            { SenseId = Id(3), Status = R.VocabularyInterpretationStatus.Rejected }] };
        var changed = knowledge.WithEntries([]); R.VocabularyLifecyclePolicy.Validate(changed);
        Assert.Empty(changed.Catalog); Assert.Empty(changed.Collections);
        var surviving = Assert.Single(changed.Interpretations); Assert.Equal(Id(6), surviving.Id);
        Assert.Null(surviving.SenseId); Assert.Equal(R.VocabularyInterpretationStatus.Candidate, surviving.Status);
        Assert.Equal(knowledge.Sources, changed.Sources); Assert.Equal(knowledge.Occurrences, changed.Occurrences);
        // Semantic reconciliation is a separate archive gate; this projection does not silently
        // edit graph restrictions or claim that lifecycle admission validates those references.
        Assert.Equal(knowledge.Semantics, changed.Semantics);
    }

    [Fact]
    public void InvalidReferencesAndConflictingAcceptanceCannotBecomeCollectedExamples()
    {
        var evidence = Evidence(); var knowledge = Knowledge();
        Reject(evidence with { Interpretations = [new(Id(5), Id(99), Now)] });
        Reject(evidence with { Interpretations = [new(Id(5), Id(2), Now) { Status = R.VocabularyInterpretationStatus.Accepted }] });
        Reject(knowledge with { Interpretations = [knowledge.Interpretations[0], knowledge.Interpretations[0] with { Id = Id(6) }] });
        Reject(knowledge with { Collections = [new(Id(99), Now)] });
        Reject(knowledge with { Collections = [new(Id(3), Now) { InterpretationIds = [Id(6)] }] });
        Reject(knowledge with { Collections = [new(Id(3), Now) { InterpretationIds = [Id(5), Id(5)] }] });
        Reject(knowledge with { Sources = [] });
        Reject(knowledge with { Sources = [knowledge.Sources[0], knowledge.Sources[0]] });
        Reject(knowledge with { Catalog = [knowledge.Catalog[0] with { Senses = [knowledge.Catalog[0].Senses[0] with { Note = "wrong owner" }] }] });
        Reject(knowledge with { Interpretations = [knowledge.Interpretations[0] with { Provenance = new() { Confidence = double.NaN } }] });
        var unicode = evidence with { Sources = [new(Id(1), "A😀B", R.VocabularySourceKind.Captured, null, Now)], Occurrences = [new(Id(2), Id(1), [new(2, 1)])] };
        Reject(unicode);
        var twoSenses = knowledge with { Catalog = [knowledge.Catalog[0] with { Senses = [knowledge.Catalog[0].Senses[0], new(Id(10), "河岸", Now, Now)] }],
            Collections = [new(Id(3), Now) { IsSavedWithoutContext = true }, new(Id(10), Now) { IsSavedWithoutContext = true }] };
        Reject(twoSenses);
    }

    [Fact]
    public void ResourceValuesDetachCallerListsAndPreserveUnknownMetadataAndPreciseEvidenceTime()
    {
        var tags = new[] { "finance" }; var ranges = new[] { new VocabularyTextRange(4, 4) };
        var example = new R.VocabularyExample(Id(5), "The bank is closed.", "", ranges, R.VocabularyExampleCompleteness.Unknown, "银行", Now)
            { SourceId = Id(1), SourceKind = R.VocabularySourceKind.Captured };
        var sense = new R.VocabularySense(Id(3), "银行", Now, Now) { Tags = tags, Examples = [example], CreatedWithoutContext = true };
        var document = R.VocabularyDocument.FromEntries([new(Id(4), "bank", "bank", null, VocabularyEntryKind.Word, [sense], Now, Now)]);
        tags[0] = "changed"; ranges[0] = new(0, 1);
        R.VocabularyLifecyclePolicy.Validate(document); Assert.Equal("finance", document.Collections[0].Tags[0]); Assert.Equal(new(4, 4), document.Occurrences[0].Ranges[0]);
        Assert.Null(document.CollectedEntries[0].Language); Assert.Equal(R.VocabularyExampleCompleteness.Unknown, document.CollectedEntries[0].Examples[0].Completeness);
        var clone = document with { Catalog = document.Catalog.ToArray(), Sources = document.Sources.ToArray(), Occurrences = document.Occurrences.ToArray(),
            Interpretations = document.Interpretations.ToArray(), Collections = document.Collections.ToArray() };
        Assert.Equal(document, clone); Assert.Equal(document.GetHashCode(), clone.GetHashCode());
        var stamp = Now.AddTicks(1); var exact = document with { Sources = [new(Id(1), example.Text, example.SourceKind, null, stamp)] };
        var reprojected = exact.WithEntries(exact.Entries); Assert.Equal(stamp, reprojected.Sources[0].EncounteredAt);
        var copied = example with { OccurrenceId = Id(2) }; copied = copied.CopiedWithNewId();
        Assert.NotEqual(example.Id, copied.Id); Assert.Equal(example.SourceId, copied.SourceId); Assert.Null(copied.OccurrenceId);
        var input = new R.LearningExplanationInput("bank", "bank", null, VocabularyEntryKind.Word, example.Text,
            example.Ranges, R.LearningExplanationContext.Unknown, "local-occurrence");
        var record = new R.LearningExplanationRecord(Id(20), input, LearningLanguage.Chinese,
            new("银行", "此处指金融机构。"), "configured model", Now);
        var annotated = document with { Interpretations = [document.Interpretations[0] with { Explanations = [record] }] };
        R.VocabularyLifecyclePolicy.Validate(annotated);
        Assert.Null(annotated.CollectedEntries[0].Examples[0].Explanations[0].Input.Language);
        Assert.Equal(R.LearningExplanationContext.Unknown, annotated.Entries[0].Explanations![0].Input.ContextKind);
        foreach (var pair in new[] { (R.LearningExplanationContext.Sentence, "sentence"), (R.LearningExplanationContext.Fragment, "fragment"),
            (R.LearningExplanationContext.Unknown, "unknown"), (R.LearningExplanationContext.None, "none") })
        {
            string json = System.Text.Json.JsonSerializer.Serialize(pair.Item1);
            Assert.Equal('"' + pair.Item2 + '"', json);
            Assert.Equal(pair.Item1, System.Text.Json.JsonSerializer.Deserialize<R.LearningExplanationContext>(json));
        }
    }
}
