using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>One occurrence in an untouched translation; ranges use the document's UTF-16 offsets.</summary>
public sealed record LearningDictionaryWord(string Text, LearningTextRange Range)
{
    public bool Equals(LearningDictionaryWord? other) => other is not null && Range == other.Range
        && LearningLexicalCollectionValue.StringEquals(Text, other.Text);
    public override int GetHashCode() => HashCode.Combine(Range, LearningLexicalCollectionValue.StringHash(Text));
}

public enum LearningUnitKind { Word, Expression, Pattern }
public enum LearningUnitStage { Pending, Ready, Failed }

/// <summary>
/// A lexical click target, independent of editor state or a proposed source correspondence. The
/// recognizer owns exact ranges; a later card/session owns presentation and dictionary lookup.
/// Arrays are detached so callers cannot mutate the index's output after publication.
/// </summary>
public sealed record LearningUnit
{
    public LearningUnit(int id, string surface, string query, LearningUnitKind kind,
        IReadOnlyList<LearningTextRange> ranges, IReadOnlyList<LearningDictionaryWord> words, string? entryId = null)
    {
        Id = id; Surface = surface; Query = query; Kind = kind;
        Ranges = Array.AsReadOnly(ranges.ToArray());
        Words = Array.AsReadOnly(words.ToArray());
        EntryId = entryId;
    }
    public int Id { get; }
    public string Surface { get; }
    public string Query { get; }
    public LearningUnitKind Kind { get; }
    public IReadOnlyList<LearningTextRange> Ranges { get; }
    public IReadOnlyList<LearningDictionaryWord> Words { get; }
    public string? EntryId { get; }

    // Swift arrays participate in value equality. Read-only .NET wrappers otherwise compare
    // by identity, so independent snapshots of the same occurrence must compare elementwise.
    public bool Equals(LearningUnit? other) => other is not null && Id == other.Id
        && LearningLexicalCollectionValue.StringEquals(Surface, other.Surface)
        && LearningLexicalCollectionValue.StringEquals(Query, other.Query) && Kind == other.Kind
        && LearningLexicalCollectionValue.StringEquals(EntryId, other.EntryId)
        && Ranges.SequenceEqual(other.Ranges) && Words.SequenceEqual(other.Words);
    public override int GetHashCode() => HashCode.Combine(Id,
        LearningLexicalCollectionValue.StringHash(Surface), LearningLexicalCollectionValue.StringHash(Query), Kind,
        LearningLexicalCollectionValue.StringHash(EntryId),
        LearningLexicalCollectionValue.Hash(Ranges), LearningLexicalCollectionValue.Hash(Words));

    public static LearningUnit Word(LearningDictionaryWord word) =>
        new(word.Range.Location, word.Text, word.Text, LearningUnitKind.Word, [word.Range], [word]);
}

/// <summary>
/// One explicit dictionary form and its source-owned senses/attribution. Recognition retains this
/// record's ID and never invents suffix forms or grammatical patterns. The offline resource owner
/// supplies these immutable snapshots; this type performs no IO or system dictionary lookup.
/// </summary>
public sealed record LearningLexiconEntry
{
    public LearningLexiconEntry(string id, string language, string headword, IReadOnlyList<string> forms,
        IReadOnlyList<LearningLexiconSense> senses, string sourceUrl,
        IReadOnlyList<string>? phonetics = null, IReadOnlyList<string>? examples = null,
        string? sourceName = null, string? licenseUrl = null, string? licenseName = null)
    {
        Id = id; Language = language; Headword = headword; SourceUrl = sourceUrl;
        Forms = Array.AsReadOnly(forms.ToArray()); Senses = Array.AsReadOnly(senses.ToArray());
        Phonetics = phonetics is null ? null : Array.AsReadOnly(phonetics.ToArray());
        Examples = examples is null ? null : Array.AsReadOnly(examples.ToArray());
        SourceName = sourceName; LicenseUrl = licenseUrl; LicenseName = licenseName;
    }
    public string Id { get; }
    public string Language { get; }
    public string Headword { get; }
    public IReadOnlyList<string> Forms { get; }
    public IReadOnlyList<LearningLexiconSense> Senses { get; }
    public string SourceUrl { get; }
    public IReadOnlyList<string>? Phonetics { get; }
    public IReadOnlyList<string>? Examples { get; }
    public string? SourceName { get; }
    public string? LicenseUrl { get; }
    public string? LicenseName { get; }

    public bool Equals(LearningLexiconEntry? other) => other is not null
        && LearningLexicalCollectionValue.StringEquals(Id, other.Id)
        && LearningLexicalCollectionValue.StringEquals(Language, other.Language)
        && LearningLexicalCollectionValue.StringEquals(Headword, other.Headword)
        && LearningLexicalCollectionValue.StringEquals(SourceUrl, other.SourceUrl)
        && LearningLexicalCollectionValue.StringEquals(SourceName, other.SourceName)
        && LearningLexicalCollectionValue.StringEquals(LicenseUrl, other.LicenseUrl)
        && LearningLexicalCollectionValue.StringEquals(LicenseName, other.LicenseName)
        && LearningLexicalCollectionValue.Equal(Forms, other.Forms) && Senses.SequenceEqual(other.Senses)
        && LearningLexicalCollectionValue.Equal(Phonetics, other.Phonetics)
        && LearningLexicalCollectionValue.Equal(Examples, other.Examples);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LearningLexicalCollectionValue.StringHash(Id)); hash.Add(LearningLexicalCollectionValue.StringHash(Language));
        hash.Add(LearningLexicalCollectionValue.StringHash(Headword)); hash.Add(LearningLexicalCollectionValue.StringHash(SourceUrl));
        hash.Add(LearningLexicalCollectionValue.StringHash(SourceName)); hash.Add(LearningLexicalCollectionValue.StringHash(LicenseUrl));
        hash.Add(LearningLexicalCollectionValue.StringHash(LicenseName));
        hash.Add(LearningLexicalCollectionValue.Hash(Forms)); hash.Add(LearningLexicalCollectionValue.Hash(Senses));
        hash.Add(LearningLexicalCollectionValue.Hash(Phonetics)); hash.Add(LearningLexicalCollectionValue.Hash(Examples));
        return hash.ToHashCode();
    }
}

/// <summary>A source's sense structure stays distinct from generated explanations and user meanings.</summary>
public sealed record LearningLexiconSense
{
    public LearningLexiconSense(string partOfSpeech, IReadOnlyList<string> definitions, IReadOnlyList<string> labels)
    {
        PartOfSpeech = partOfSpeech;
        Definitions = Array.AsReadOnly(definitions.ToArray()); Labels = Array.AsReadOnly(labels.ToArray());
    }
    public string PartOfSpeech { get; }
    public IReadOnlyList<string> Definitions { get; }
    public IReadOnlyList<string> Labels { get; }

    public bool Equals(LearningLexiconSense? other) => other is not null
        && LearningLexicalCollectionValue.StringEquals(PartOfSpeech, other.PartOfSpeech)
        && LearningLexicalCollectionValue.Equal(Definitions, other.Definitions)
        && LearningLexicalCollectionValue.Equal(Labels, other.Labels);
    public override int GetHashCode() => HashCode.Combine(LearningLexicalCollectionValue.StringHash(PartOfSpeech),
        LearningLexicalCollectionValue.Hash(Definitions), LearningLexicalCollectionValue.Hash(Labels));
}

/// <summary>Ordered collection values retain the Mac distinction between an absent and empty list.</summary>
internal static class LearningLexicalCollectionValue
{
    public static bool Equal<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, Comparer<T>());
    public static int Hash<T>(IReadOnlyList<T>? values)
    {
        var hash = new HashCode();
        hash.Add(values is not null);
        if (values is not null) foreach (var value in values) hash.Add(value, Comparer<T>());
        return hash.ToHashCode();
    }
    public static bool StringEquals(string? left, string? right) => CanonicalStringComparer.Instance.Equals(left, right);
    public static int StringHash(string? value) => value is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(value);
    private static IEqualityComparer<T> Comparer<T>() => typeof(T) == typeof(string)
        ? (IEqualityComparer<T>)(object)CanonicalStringComparer.Instance : EqualityComparer<T>.Default;
}
