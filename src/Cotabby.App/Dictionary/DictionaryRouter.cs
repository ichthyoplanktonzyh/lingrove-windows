using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Dictionaries;

/// <summary>Borrowed by all dictionary sessions. Each lookup is authorized by its captured
/// settings snapshot; this router owns neither a language model nor an implicit network provider.</summary>
internal sealed class DictionaryRouter(DictionarySettings settings, ILearningDictionaryProviding bundled, MDictLibraryService books,
    Func<string, LearningLanguage?, IReadOnlyList<LearningDictionaryWord>> words)
    : ILearningDictionaryRouter, ILearningUnknownDictionaryProviding
{
    public DictionaryConfiguration Configuration => settings.Configuration;
    public int CountWords(string text, LearningLanguage? language) => words(text, language).Count;
    public event Action<DictionaryConfiguration>? ConfigurationChanged { add => settings.Changed += value; remove => settings.Changed -= value; }
    public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default) =>
        bundled.LookupAsync(query, language, expression, cancellationToken);
    public Task<LearningDictionaryResult> LookupUnknownAsync(string query, bool expression, CancellationToken cancellationToken = default) =>
        bundled is ILearningUnknownDictionaryProviding unknown ? unknown.LookupUnknownAsync(query, expression, cancellationToken)
            : Task.FromException<LearningDictionaryResult>(new InvalidOperationException("The dictionary is unavailable."));
    public Task<LearningDictionaryResult> LookupSourceAsync(string query, LearningLanguage? language, bool expression,
        DictionarySource source, DictionaryConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DictionaryContentPolicy.ValidQuery(query) || !configuration.Sources(language).Contains(source)) return Task.FromResult(LearningDictionaryResult.NotFound);
        return source.Kind switch
        {
            "bundled" => language is { } known ? LookupAsync(query, known, expression, cancellationToken) : LookupUnknownAsync(query, expression, cancellationToken),
            "mdict" when configuration.Libraries.FirstOrDefault(x => x.Source == source) is { } book => books.LookupAsync(query, book, cancellationToken),
            _ => Task.FromException<LearningDictionaryResult>(new InvalidOperationException("This dictionary source is unavailable on Windows.")),
        };
    }
    public Task<DictionaryResource?> ResourceAsync(string path, Guid libraryId, DictionaryConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var source = DictionarySource.MDict(libraryId); var book = configuration.Libraries.FirstOrDefault(x => x.Source == source);
        return configuration.Enabled.Contains(source) && book is not null ? books.ResourceAsync(path, book, cancellationToken) : Task.FromResult<DictionaryResource?>(null);
    }
}
