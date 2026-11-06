using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI;

/// <summary>Reviewed example answers, used only by the mounted demo's real sessions.</summary>
internal sealed class LearningShowcaseExplainer : ILearningExplanationGenerating
{
    private readonly Dictionary<LearningExplanationInput, LearningExplanationRecord> _saved = [];
    public event Action? Changed { add { } remove { } }
    public LearningLanguage Language => LearningLanguage.Chinese;
    public bool IsConfigured => true;
    public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = input.Target switch
        {
            "请随时告诉我项目的最新进展。" => new LearningExplanationResult("Please keep me posted on the project's progress.",
                "这句话礼貌地请求对方持续告知项目进展。Please 表示礼貌请求；keep me posted 是固定表达。"),
            "bank" when input.Context.Contains("river", StringComparison.OrdinalIgnoreCase) => new("河岸", "这里的 bank 指河流旁的陆地。"),
            "bank" => new("银行", "在 deposited money 的语境中，bank 指提供存款服务的银行。"),
            "keep me posted" or "keep someone posted" => new("随时告知某人最新进展", "Keep someone informed about the latest developments."),
            "posted" => new("获知最新情况", "这里的 posted 表示 informed，不是邮寄。"),
            _ => new(input.Target, "此演示仅包含固定示例；实际查词使用你所选择的 AI 和词典来源。"),
        };
        var record = new LearningExplanationRecord(Guid.NewGuid(), input, Language, result, "Sample explanation", DateTimeOffset.UnixEpoch);
        lock (_saved) _saved[input] = record;
        return Task.FromResult(record);
    }
    public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) { lock (_saved) return _saved.GetValueOrDefault(input); }
    public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult(SavedRecord(input));
    public void RecordDisplay(string requestId) { }
}

/// <summary>Both example sources are fixed local text; no system dictionary or service is called.</summary>
internal sealed class LearningShowcaseDictionary : ILearningDictionaryRouter
{
    public DictionaryConfiguration Configuration { get; } = new();
    public event Action<DictionaryConfiguration>? ConfigurationChanged { add { } remove { } }
    public int CountWords(string text, LearningLanguage? language) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default) =>
        LookupSourceAsync(query, language, expression, DictionarySource.Bundled, Configuration, cancellationToken);
    public Task<LearningDictionaryResult> LookupSourceAsync(string query, LearningLanguage? language, bool expression,
        DictionarySource source, DictionaryConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string definition = query.ToLowerInvariant() switch
        {
            "keep me posted" or "keep someone posted" => "Keep someone informed about new developments.",
            "posted" => "Informed about recent developments.",
            "bank" => "A financial institution, or land beside a river.",
            _ => "This example contains only fixed sample entries.",
        };
        var entry = new LearningLexiconEntry("sample", "en", query, [],
            [new(expression ? "verb phrase" : "word", [definition], [])], "", sourceName: "Sample dictionary");
        return Task.FromResult<LearningDictionaryResult>(new LearningDictionaryResult.Entries([entry]));
    }
    public Task<DictionaryResource?> ResourceAsync(string path, Guid libraryId, DictionaryConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult<DictionaryResource?>(null);
}
