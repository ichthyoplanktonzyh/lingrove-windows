using System.Text;
using Cotabby.Core.Support;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// App-owned offline store shared by recognition and bundled lookup, adapted from the Mac actor.
/// One worker gate serializes lazy source/index loading away from the UI. Only successful loads
/// are cached; Chinese scripts share entries but retain the requested language for segmentation.
/// There is no HTTP client or system-dictionary fallback in this boundary.
/// </summary>
public sealed class LearningLexiconService(ILearningLexiconResourceSource source, ILearningLexicalText text)
    : ILearningUnitRecognizing, ILearningDictionaryProviding
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LearningUnitRecognizer> _indices = new(StringComparer.Ordinal);
    private LearningLexiconResource? _resource;

    public Task<IReadOnlyList<LearningUnit>> RecognizeAsync(string translation, LearningLanguage language,
        CancellationToken cancellationToken = default) => SerializedAsync(() =>
        {
            var units = Index(language).Recognize(translation, language);
            // The Mac recognition method checks again after synchronous resource/index work.
            cancellationToken.ThrowIfCancellationRequested();
            return units;
        }, cancellationToken);

    public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression,
        CancellationToken cancellationToken = default) => SerializedAsync<LearningDictionaryResult>(() =>
        {
            if (string.IsNullOrEmpty(query) || Encoding.UTF8.GetByteCount(query) > 256
                || !InsertionSafetyGate.IsSafeToInsert(query))
                throw new LearningLexiconException(LearningLexiconFailure.InvalidQuery);
            var entries = Index(language).Lookup(query)
                .Where(entry => expression || text.Key(entry.Headword) == text.Key(query)).ToArray();
            // Bundled Mac lookup checks cancellation on entry only. Its result is not editor
            // authority: the owning card/session must still reject retired request identities.
            return entries.Length == 0 ? LearningDictionaryResult.NotFound : new LearningDictionaryResult.Entries(entries);
        }, cancellationToken);

    private LearningUnitRecognizer Index(LearningLanguage language)
    {
        if (!Enum.IsDefined(language)) throw new LearningLexicalException(LearningLexicalFailure.InvalidInput);
        string code = language is LearningLanguage.Chinese or LearningLanguage.TraditionalChinese ? "zh" : language.Code();
        if (_indices.TryGetValue(code, out var index)) return index;
        _resource ??= source.Load();
        index = new LearningUnitRecognizer(_resource.Entries.Where(entry => entry.Language == code).ToArray(), text);
        _indices.Add(code, index);
        return index;
    }

    private async Task<T> SerializedAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
