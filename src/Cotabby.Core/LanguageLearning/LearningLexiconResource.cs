namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// One immutable offline dataset, including its source-level attribution. The lexicon service
/// owns this snapshot after its first successful load; language indices retain the same entries.
/// It has no editor state, vocabulary identity, download behavior or generated definitions.
/// </summary>
public sealed class LearningLexiconResource(string version, string attribution, string licenseUrl,
    IReadOnlyList<LearningLexiconEntry> entries)
{
    public const int MaximumBytes = 64_000_000;
    public string Version { get; } = version;
    public string Attribution { get; } = attribution;
    public string LicenseUrl { get; } = licenseUrl;
    public IReadOnlyList<LearningLexiconEntry> Entries { get; } = Array.AsReadOnly(entries.ToArray());
}

/// <summary>Called only on the serialized worker; implementations own bounded local resource IO.</summary>
public interface ILearningLexiconResourceSource
{
    LearningLexiconResource Load();
}

public interface ILearningUnitRecognizing
{
    Task<IReadOnlyList<LearningUnit>> RecognizeAsync(string translation, LearningLanguage language,
        CancellationToken cancellationToken = default);
}

/// <summary>The bundled-only lookup contract; other Mac provider/source variants remain unported.</summary>
public interface ILearningDictionaryProviding
{
    Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression,
        CancellationToken cancellationToken = default);
}

/// <summary>Bundled lookup with no identified language searches all resource languages.</summary>
public interface ILearningUnknownDictionaryProviding
{
    Task<LearningDictionaryResult> LookupUnknownAsync(string query, bool expression,
        CancellationToken cancellationToken = default);
}
/// <summary>Optional shared source router. Requests carry the immutable configuration that
/// authorized them; changes retire each card's in-flight work and cached results.</summary>
public interface ILearningDictionaryRouter : ILearningDictionaryProviding
{
    DictionaryConfiguration Configuration { get; }
    event Action<DictionaryConfiguration>? ConfigurationChanged;
    int CountWords(string text, LearningLanguage? language);
    Task<LearningDictionaryResult> LookupSourceAsync(string query, LearningLanguage? language, bool expression,
        DictionarySource source, DictionaryConfiguration configuration, CancellationToken cancellationToken = default);
    Task<DictionaryResource?> ResourceAsync(string path, Guid libraryId, DictionaryConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// The bundled subset of the Mac result algebra. A miss is a value, whereas an unavailable
/// resource throws. System articles, imported dictionaries and website results remain unported.
/// </summary>
public abstract record LearningDictionaryResult
{
    private LearningDictionaryResult() { }
    public static LearningDictionaryResult NotFound { get; } = new Missing();
    private sealed record Missing : LearningDictionaryResult;
    /// <summary>An explicit external browser offer; constructing it does not navigate.</summary>
    public sealed record Website(Uri Url) : LearningDictionaryResult;
    /// <summary>Third-party HTML remains untrusted; a renderer must establish its own isolation.</summary>
    public sealed record Article(string Html, string Text, string SourceName, Guid? ResourceLibrary = null,
        DictionaryLanguagePair? Languages = null) : LearningDictionaryResult;
    public sealed record Entries : LearningDictionaryResult
    {
        public Entries(IReadOnlyList<LearningLexiconEntry> values) => Values = Array.AsReadOnly(values.ToArray());
        public IReadOnlyList<LearningLexiconEntry> Values { get; }
        public bool Equals(Entries? other) => other is not null && Values.SequenceEqual(other.Values);
        public override int GetHashCode() => LearningLexicalCollectionValue.Hash(Values);
    }
}

public enum LearningLexiconFailure { MissingResource, UnreadableResource, InvalidQuery }

/// <summary>Fixed Mac-facing failure messages; input text, file paths and parser details stay private.</summary>
public sealed class LearningLexiconException(LearningLexiconFailure failure) : Exception(failure switch
{
    LearningLexiconFailure.MissingResource => "The local expression dictionary is unavailable. You can still look up individual words.",
    LearningLexiconFailure.UnreadableResource => "The local expression dictionary could not be read. You can still look up individual words.",
    _ => "This lookup text is unavailable.",
})
{
    public LearningLexiconFailure Failure { get; } = failure;
}
