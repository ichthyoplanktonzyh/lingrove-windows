using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Inference.Tests.Vocabulary;

/// <summary>Real SQLite in an isolated private temporary folder; never the user's notebook.</summary>
public sealed class FrozenVocabularySQLiteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-v8-sqlite-" + Guid.NewGuid().ToString("N"));
    private string Pathname => Path.Combine(_root, "vocabulary.sqlite");
    public FrozenVocabularySQLiteTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static F.VocabularyDocument Graph()
    {
        var time = new F.SourceDate(814759445.12345672); var sense = new F.VocabularySense(Id(2), "greeting", time, time);
        var entry = new F.VocabularyEntry(Id(1), "hello", "hello", null, VocabularyEntryKind.Word, [sense], time, time);
        var node = new F.VocabularyMeaningNode(Id(6), [new("Greeting") { Id = Id(7), Language = LearningLanguage.English }]) { CreatedAt = time, UpdatedAt = time };
        var category = new R.VocabularyCategory(Id(9), R.VocabularyCategoryAxis.Content, [new("Category") { Id = Id(10) }]);
        return new() { Catalog = [entry], Sources = [new(Id(3), "hello", R.VocabularySourceKind.Imported, null, time), new(Id(12), "orphan evidence", R.VocabularySourceKind.Captured, null, time)],
            Occurrences = [new(Id(4), Id(3), [new(0,5)])], Interpretations = [new F.VocabularyInterpretation(Id(5),Id(4),time) { SenseId = Id(2), Status = R.VocabularyInterpretationStatus.Accepted }],
            Collections = [new F.VocabularyCollectionItem(Id(2),time) { InterpretationIds = [Id(5)], Content = [] }],
            Semantics = new() { Nodes = [node], Mappings = [new(node.Id,Id(2)) { Id = Id(8), EvidenceIds = [Id(5)] }] },
            Classification = new() { Categories = [category], Memberships = [new(Id(11),node.Id,category.Id)] } };
    }
    private static F.VocabularyDocument RichGraph()
    {
        var graph = Graph(); var time = graph.Sources[0].EncounteredAt; var sense = graph.Catalog[0].Senses[0] with { CreatedWithoutContext = true };
        var general = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.None, "sense:" + Id(2).ToString("D").ToUpperInvariant());
        var contextual = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.Unknown, "c4a7706c333e599abcbeb7c0fb75d8fdeb9ccb662ea94ba0fcc814ad1dccbe22");
        var result = new LearningExplanationResult("greeting", "A greeting.");
        sense = sense with { Explanations = [new F.LearningExplanationRecord(Id(20),general,LearningLanguage.English,result,"local",time)] };
        var claim = graph.Interpretations[0] with { Explanations = [new F.LearningExplanationRecord(Id(21),contextual,LearningLanguage.English,result,"local",time)] };
        var role = new R.VocabularyMeaningRole("person") { Id = Id(22) }; var node = graph.Semantics.Nodes[0] with { Roles = [role] };
        var node2 = new F.VocabularyMeaningNode(Id(23), [new("Other") { Id = Id(24) }]) { CreatedAt = time, UpdatedAt = time };
        var slot = new R.VocabularyPatternSlot("person") { Id = Id(25) };
        var pattern = new R.VocabularyUsagePattern(Id(2),"hello [person]") { Id = Id(26), Slots = [slot] };
        var mapping = graph.Semantics.Mappings[0] with { PatternId = pattern.Id, RoleBindings = [new(role.Id,slot.Id) { Id = Id(27) }] };
        var cue = new R.VocabularyRetrievalCue("greeting") { Id = Id(28) };
        var semantic = graph.Semantics with { Nodes = [node,node2], Patterns = [pattern], Mappings = [mapping], Relations = [new(node.Id,node2.Id) { Id = Id(29) }],
            Cues = [cue], CueTargets = [new(cue.Id,node.Id) { Id = Id(30) }] };
        var plan = new ReviewPlan(Id(31)) { Layout = new() };
        var binding = new F.AnkiNoteBinding(Id(32),Id(2),"http://localhost:8765","Default",1,"Model","Deck","LingroveID",
            new Dictionary<ReviewField,string> { [ReviewField.Identity] = "LingroveID" }, new Dictionary<string,string> { ["LingroveID"] = Id(2).ToString("D") }, time);
        var review = new F.VocabularyReview(plan,[binding],binding.Id);
        var content = new VocabularyContentBlock(Id(33), new VocabularyBlockContent.Text(new("authored")));
        var collection = graph.Collections[0] with { Content = [content], Review = review, ReviewCards = [] };
        var first = graph.Classification.Categories[0] with { FrameworkId = Id(34), Key = "root" };
        var child = new R.VocabularyCategory(Id(35), R.VocabularyCategoryAxis.Content, [new("Child") { Id = Id(36) }]);
        var classification = graph.Classification with { Frameworks = [new(Id(34),"framework",1)], Categories = [first,child],
            Parents = [new(Id(37),child.Id,first.Id)], Overrides = [new(first.Id) { Definition = "local" }] };
        return graph with { Catalog = [graph.Catalog[0] with { Senses = [sense] }], Interpretations = [claim], Collections = [collection],
            Semantics = semantic, Classification = classification, ReviewTemplates = [new(Id(38),"template",new())] };
    }
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void MislabeledOrIncompleteSchemaIsRejectedBeforeMutation(int version)
    {
        using (var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname)) { }
        using (var connection = VocabularySQLiteConnection.OpenNamed(Pathname))
        {
            if (version == 4)
                foreach (var table in new[] { "category_overrides", "category_memberships", "category_parents", "categories", "classification_frameworks" }) connection.Execute("DROP TABLE " + table);
            connection.Execute("DROP INDEX entries_language"); connection.Execute("PRAGMA user_version=" + version);
        }
        var error = Assert.ThrowsAny<VocabularyException>(() => { using var ignored = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); });
        Assert.Equal(VocabularyFailure.InvalidArchive, error.Failure);
        using var check = VocabularySQLiteConnection.OpenNamed(Pathname); Assert.Equal(version, check.Scalar("PRAGMA user_version"));
        Assert.Empty(check.Rows("SELECT name FROM sqlite_master WHERE type='index' AND name='entries_language'"));
        if (version == 4) Assert.Empty(check.Rows("SELECT name FROM sqlite_master WHERE type='table' AND name='classification_frameworks'"));
    }
    [Fact]
    public void SchemaContainsFrozenTableAndForeignKeyStructure()
    {
        using var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); using var connection = VocabularySQLiteConnection.OpenNamed(Pathname);
        Assert.Equal(31, connection.Rows("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").Length);
        Assert.Equal(5, connection.Scalar("PRAGMA user_version")); Assert.Equal(0x4C47564E, connection.Scalar("PRAGMA application_id"));
        var mappingSql = Assert.IsType<VocabularySQLiteConnection.Value.Text>(connection.Rows("SELECT sql FROM sqlite_master WHERE type='table' AND name='meaning_mappings'")[0][0]).Data;
        Assert.Contains("FOREIGN KEY(pattern_id,parent_id) REFERENCES usage_patterns(id,parent_id)", mappingSql);
        var usages = connection.Rows("PRAGMA foreign_key_list(usages)"); Assert.Equal(2, usages.Length);
        Assert.Single(connection.Rows("SELECT name FROM sqlite_master WHERE type='index' AND name='meaning_preferred_language'"));
    }
    [Fact]
    public void LookupAndPreferredLabelReplacementsAreAtomic()
    {
        using var db = FrozenVocabularySQLiteDatabase.CreateMemory(); var original = Graph();
        var time = original.Sources[0].EncounteredAt;
        var second = new F.VocabularyEntry(Id(39), "other", "other", null, VocabularyEntryKind.Word, [], time, time);
        original = original with { Catalog = [original.Catalog[0], second] }; db.Write(original, new(), 0);
        var oldNode = original.Semantics.Nodes[0]; var oldLabel = oldNode.Labels[0];
        var labels = new[] { oldLabel with { Preferred = false }, new R.VocabularyMeaningLabel("New greeting") { Id = Id(40), Language = LearningLanguage.English } };
        var next = original with { Catalog = [original.Catalog[0] with { Expression = "other", DictionaryForm = "other" }, second with { Expression = "hello", DictionaryForm = "hello" }],
            Semantics = original.Semantics with { Nodes = [oldNode with { Labels = labels }] } };
        Assert.Equal(2UL, db.Write(next, original, 1)); Assert.Equal(next, db.Read().Document);
    }
    [Fact]
    public async Task CancellationAfterRowsWereWrittenRollsBackTheTransaction()
    {
        var connection = VocabularySQLiteConnection.OpenMemory(); FrozenVocabularySQLiteDatabase.CreateSchema(connection);
        using var db = FrozenVocabularySQLiteDatabase.Admit(connection); using var cancellation = new CancellationTokenSource();
        var time = new F.SourceDate(0); var graph = new F.VocabularyDocument
        { Sources = Enumerable.Range(0, 4000).Select(i => new F.VocabularySourceSnapshot(Guid.NewGuid(), "evidence", R.VocabularySourceKind.Imported, null, time)).ToArray() };
        long before = connection.TotalChanges;
        var write = Task.Run(() => db.Write(graph, new(), 0, cancellationToken: cancellation.Token));
        Assert.True(SpinWait.SpinUntil(() => connection.TotalChanges > before || write.IsCompleted, TimeSpan.FromSeconds(30)));
        Assert.False(write.IsCompleted); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await write);
        Assert.Equal(0UL, db.Revision()); Assert.Empty(db.Read().Document.Sources);
    }
    [Fact]
    public void CompleteFrozenGraphExercisesEveryRelationalTableAndReopens()
    {
        var document = RichGraph(); var rows = F.VocabularyDatabaseProjection.Rows(document);
        Assert.Equal(Enum.GetValues<F.VocabularyDatabaseTable>().Order(), rows.Values.Select(x => x.Table).Distinct().Order());
        using (var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname)) Assert.Equal(1UL, db.Write(document,new(),0));
        using var reopened = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); Assert.Equal(document, reopened.Read().Document);
    }
    [Fact]
    public void NamedFileReopenPreservesWholeGraphAndSourceDouble()
    {
        var source = Graph();
        using (var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname))
        { Assert.Equal(1UL, db.Write(source, new(), 0)); Assert.Equal(source, db.Read().Document); }
        using var reopened = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); var snapshot = reopened.Read();
        Assert.Equal(1UL, snapshot.Revision); Assert.Equal(source, snapshot.Document);
        Assert.Equal(BitConverter.DoubleToInt64Bits(source.Sources[0].EncounteredAt.ReferenceSeconds), BitConverter.DoubleToInt64Bits(snapshot.Document.Sources[0].EncounteredAt.ReferenceSeconds));
        Assert.Equal(2, snapshot.Document.Sources.Count); Assert.Empty(Directory.GetFiles(_root, ".vocabulary-*"));
    }
    [Fact]
    public void StaleRevisionCancellationAndCommitFailureDoNotChangeTheGraph()
    {
        var connection = VocabularySQLiteConnection.OpenMemory(); FrozenVocabularySQLiteDatabase.CreateSchema(connection);
        using var db = FrozenVocabularySQLiteDatabase.Admit(connection); var original = Graph(); db.Write(original, new(), 0);
        var changed = original with { Collections = [original.Collections[0] with { Note = "changed" }] };
        var conflict = Assert.Throws<VocabularyException>(() => db.Write(changed, original, 0)); Assert.Equal(VocabularyFailure.EditConflict, conflict.Failure);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => db.Write(changed, original, 1, cancellationToken: cancelled.Token));
        connection.Execute("CREATE TRIGGER inject_bad_occurrence AFTER UPDATE ON state BEGIN UPDATE usages SET occurrence_id='missing'; END");
        Assert.ThrowsAny<VocabularyException>(() => db.Write(changed, original, 1));
        Assert.Equal(1UL, db.Revision()); Assert.Equal(original, db.Read().Document);
    }
    [Fact]
    public void SchemaFourMigrationIsAtomicAndRetainsState()
    {
        var original = Graph() with { Classification = new() };
        using (var writer = FrozenVocabularySQLiteDatabase.OpenFile(Pathname)) writer.Write(original, new(), 0);
        using (var connection = VocabularySQLiteConnection.OpenNamed(Pathname))
        {
            foreach (var table in new[] { "category_overrides", "category_memberships", "category_parents", "categories", "classification_frameworks" }) connection.Execute("DROP TABLE " + table);
            connection.Execute("PRAGMA user_version=4");
        }
        using var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname);
        Assert.Equal(1UL, db.Revision()); Assert.Equal(original, db.Read().Document);
        using var check = VocabularySQLiteConnection.OpenNamed(Pathname); Assert.Equal(5, check.Scalar("PRAGMA user_version"));
    }
    [Fact]
    public void FailedSchemaFourMigrationRollsBackEveryAddedTableAndVersion()
    {
        using (var db = FrozenVocabularySQLiteDatabase.OpenFile(Pathname)) { }
        using (var connection = VocabularySQLiteConnection.OpenNamed(Pathname))
        {
            foreach (var table in new[] { "category_overrides", "category_memberships", "category_parents", "categories", "classification_frameworks" }) connection.Execute("DROP TABLE " + table);
            connection.Execute("CREATE TABLE categories (id TEXT)"); connection.Execute("PRAGMA user_version=4");
        }
        Assert.ThrowsAny<VocabularyException>(() => { using var rejected = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); });
        using var check = VocabularySQLiteConnection.OpenNamed(Pathname); Assert.Equal(4, check.Scalar("PRAGMA user_version"));
        Assert.Empty(check.Rows("SELECT name FROM sqlite_master WHERE type='table' AND name='classification_frameworks'"));
    }
    [Fact]
    public void ExistingVersionTwoFileIsRejectedWithoutModification()
    {
        using (var old = VocabularySQLiteDatabase.OpenFile(Pathname)) { Assert.Equal(2, VocabularySQLiteDatabase.SchemaVersion); }
        var before = File.ReadAllBytes(Pathname);
        var error = Assert.Throws<VocabularyException>(() => { using var rejected = FrozenVocabularySQLiteDatabase.OpenFile(Pathname); });
        Assert.Equal(VocabularyFailure.UnsupportedVersion, error.Failure); Assert.Equal(before, File.ReadAllBytes(Pathname));
        using var reopened = VocabularySQLiteDatabase.OpenFile(Pathname); Assert.Equal(0UL, reopened.Revision());
    }
    [Fact]
    public void AggregateReadBudgetRejectsWithoutChangingDatabase()
    {
        using var db = FrozenVocabularySQLiteDatabase.CreateMemory();
        var source = RichGraph(); db.Write(source, new(), 0);
        var rows = F.VocabularyDatabaseProjection.Rows(source);
        var writeError = Assert.Throws<VocabularyException>(() => FrozenVocabularySQLiteDatabase.EnsureReadBudget(rows.Values, 512));
        Assert.Equal(VocabularyFailure.OversizedArchive, writeError.Failure);
        var storedError = Assert.Throws<VocabularyException>(() => db.CheckStoredReadBudget(512));
        Assert.Equal(VocabularyFailure.OversizedArchive, storedError.Failure);
        var error = Assert.Throws<VocabularyException>(() => db.Read(maximumBytes: 512));
        Assert.Equal(VocabularyFailure.OversizedArchive, error.Failure);
        Assert.Equal(1UL, db.Revision()); Assert.Equal(source, db.Read().Document);
    }
    [Fact]
    public void StreamingStopsOnCancellationBetweenRows()
    {
        using var connection = VocabularySQLiteConnection.OpenMemory();
        connection.Execute("CREATE TABLE probes (value TEXT)");
        for (int i = 0; i < 20; i++) connection.Execute("INSERT INTO probes VALUES ('data')");
        using var cancel = new CancellationTokenSource(); int seen = 0;
        Assert.Throws<OperationCanceledException>(() => connection.VisitRows("SELECT value FROM probes", [], cancel.Token, null,
            _ => { if (++seen == 3) cancel.Cancel(); }));
        Assert.Equal(3, seen); Assert.Equal(20, connection.Scalar("SELECT count(*) FROM probes"));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
