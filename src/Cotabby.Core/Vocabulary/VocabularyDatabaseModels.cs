using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>Mac relational ownership tables, ordered parent-first for deferred foreign-key writes.</summary>
public enum VocabularyDatabaseTable { Entries, Senses, Usages, SenseExplanations, UsageExplanations, Blocks, Reviews, Bindings, Templates }

/// <summary>Closed table metadata, shared by projection and a future SQLite adapter; no SQL executes here.</summary>
public static class VocabularyDatabaseTables
{
    public static VocabularyDatabaseTable? Parent(this VocabularyDatabaseTable table) => table switch
    {
        VocabularyDatabaseTable.Entries or VocabularyDatabaseTable.Templates => null,
        VocabularyDatabaseTable.Senses => VocabularyDatabaseTable.Entries,
        VocabularyDatabaseTable.Usages or VocabularyDatabaseTable.SenseExplanations or VocabularyDatabaseTable.Blocks or VocabularyDatabaseTable.Reviews => VocabularyDatabaseTable.Senses,
        VocabularyDatabaseTable.UsageExplanations => VocabularyDatabaseTable.Usages,
        VocabularyDatabaseTable.Bindings => VocabularyDatabaseTable.Reviews,
        _ => throw new VocabularyException(VocabularyFailure.InvalidArchive),
    };
    public static string Name(this VocabularyDatabaseTable table) => table switch
    {
        VocabularyDatabaseTable.Entries => "entries", VocabularyDatabaseTable.Senses => "senses", VocabularyDatabaseTable.Usages => "usages",
        VocabularyDatabaseTable.SenseExplanations => "sense_explanations", VocabularyDatabaseTable.UsageExplanations => "usage_explanations",
        VocabularyDatabaseTable.Blocks => "blocks", VocabularyDatabaseTable.Reviews => "reviews", VocabularyDatabaseTable.Bindings => "bindings",
        VocabularyDatabaseTable.Templates => "templates", _ => throw new VocabularyException(VocabularyFailure.InvalidArchive),
    };
    public static IReadOnlyList<string> ExtraColumns(this VocabularyDatabaseTable table) => table switch
    {
        VocabularyDatabaseTable.Entries => Array.AsReadOnly(new[] { "lookup_key", "language", "expression" }),
        VocabularyDatabaseTable.Senses => Array.AsReadOnly(new[] { "meaning" }),
        _ when Enum.IsDefined(table) => Array.Empty<string>(),
        _ => throw new VocabularyException(VocabularyFailure.InvalidArchive),
    };
}

/// <summary>Entity table plus exact storage ID; explanation/binding IDs also contain their owning row ID.</summary>
public readonly record struct VocabularyDatabaseKey(VocabularyDatabaseTable Table, string Id)
{
    public bool Equals(VocabularyDatabaseKey other) => Table == other.Table && CollectionValues.Same(Id, other.Id);
    public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Id));
}

/// <summary>
/// Typed leaf/header alternatives keep relationship stripping lossless and table decoding explicit.
/// A future codec encodes one alternative's value, not a second domain graph. These cases own no
/// database handle and delegate value equality to the existing immutable notebook records.
/// </summary>
public abstract record VocabularyDatabasePayload
{
    private protected VocabularyDatabasePayload() { }
    public sealed record Entry(VocabularyEntry Value) : VocabularyDatabasePayload;
    public sealed record Sense(VocabularySense Value) : VocabularyDatabasePayload;
    public sealed record Usage(VocabularyExample Value) : VocabularyDatabasePayload;
    public sealed record Explanation(LearningExplanationRecord Value) : VocabularyDatabasePayload;
    public sealed record Block(VocabularyContentBlock Value) : VocabularyDatabasePayload;
    public sealed record Review(VocabularyReview Value) : VocabularyDatabasePayload;
    public sealed record Binding(AnkiNoteBinding Value) : VocabularyDatabasePayload;
    public sealed record Template(ReviewLayoutTemplate Value) : VocabularyDatabasePayload;
}

/// <summary>
/// Immutable projected row: parent and position own relationships; Payload contains one entity's
/// scalar/leaf metadata. Extra indexed columns are rechecked by roundtrip projection on read.
/// SQLite may compare row values before encoding so an unrelated note edit need not rewrite usages.
/// </summary>
public sealed record VocabularyDatabaseRecord
{
    private IReadOnlyList<string> _extra = Array.Empty<string>();
    public VocabularyDatabaseRecord(VocabularyDatabaseTable table, string id, string? parentId, int position, VocabularyDatabasePayload payload,
        IReadOnlyList<string>? extra = null)
    { Table = table; Id = id; ParentId = parentId; Position = position; Payload = payload; Extra = extra ?? []; }
    public VocabularyDatabaseTable Table { get; init; }
    public string Id { get; init; }
    public string? ParentId { get; init; }
    public int Position { get; init; }
    public VocabularyDatabasePayload Payload { get; init; }
    public IReadOnlyList<string> Extra { get => _extra; init => _extra = CollectionValues.List(value); }
    public VocabularyDatabaseKey Key => new(Table, Id);
    public bool Equals(VocabularyDatabaseRecord? other) => other is not null && Table == other.Table && CollectionValues.Same(Id, other.Id)
        && CollectionValues.Same(ParentId, other.ParentId) && Position == other.Position && Payload == other.Payload && CollectionValues.SequenceEqual(Extra, other.Extra);
    public override int GetHashCode() => HashCode.Combine(Table, CollectionValues.StringHash(Id), CollectionValues.StringHash(ParentId), Position, Payload, CollectionValues.SequenceHash(Extra));
}
