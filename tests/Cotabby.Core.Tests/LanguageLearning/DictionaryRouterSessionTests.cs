using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionaryRouterSessionTests
{
    private static readonly LearningUnit Unit = new(0, "bank", "bank", LearningUnitKind.Word, [new(0, 4)], [new("bank", new(0, 4))], "fixture");
    [Fact]
    public async Task AutomaticFallbackSelectsLocalBookAndExplicitSwitchUsesItsOwnCache()
    {
        var router = new Router(); await using var session = new LearningDictionarySession(router);
        await session.OpenAsync(Unit, "fixture", LearningLanguage.English);
        Assert.Equal(router.Book.Source, session.State.SelectedSource); Assert.IsType<LearningDictionaryResult.Article>(session.State.Result);
        Assert.Contains(DictionarySource.System, router.Calls); Assert.Contains(DictionarySource.Bundled, router.Calls);
        await session.SelectSourceAsync(DictionarySource.Bundled); Assert.Same(LearningDictionaryResult.NotFound, session.State.Result);
        int previous = router.Calls.Count; await session.SelectSourceAsync(DictionarySource.Bundled); Assert.Equal(previous, router.Calls.Count);
        router.Set(new DictionaryConfiguration(enabled: [DictionarySource.Bundled]));
        for (int i = 0; i < 100 && session.State.AvailableSources.Count != 1; i++) await Task.Delay(10);
        Assert.Equal([DictionarySource.Bundled], session.State.AvailableSources);
        await session.SelectSourceAsync(router.Book.Source); Assert.NotEqual(router.Book.Source, session.State.SelectedSource);
    }
    [Fact]
    public async Task ResourceCannotPublishAfterConfigurationRetirement()
    {
        var router = new Router(); await using var session = new LearningDictionarySession(router);
        await session.OpenAsync(Unit, "fixture", LearningLanguage.English);
        var pending = session.LoadResourceAsync("image.png", router.Book.Id, session.State.RequestIdentity);
        await router.ResourceEntered.Task; router.Set(new DictionaryConfiguration(enabled: [DictionarySource.Bundled]));
        router.ResourceResult.SetResult(new([1, 2], "image/png")); Assert.Null(await pending);
    }
    [Fact]
    public async Task ReferenceNavigatesWithinSelectedSourceAndBackRestoresOriginalTarget()
    {
        var router = new Router(); await using var session = new LearningDictionarySession(router);
        await session.OpenAsync(Unit, "fixture", LearningLanguage.English);
        var oldTarget = session.State.TargetIdentity;
        await session.FollowReferenceAsync("river bank", oldTarget);
        Assert.Equal("river bank", session.State.Query);
        Assert.Equal(router.Book.Source, session.State.SelectedSource);
        Assert.NotEqual(oldTarget, session.State.TargetIdentity);
        var newTarget = session.State.TargetIdentity;
        await session.FollowReferenceAsync("stale", oldTarget);
        Assert.Equal("river bank", session.State.Query);
        await session.ReturnToExpressionAsync(newTarget);
        Assert.Equal("bank", session.State.Query);
        Assert.Null(session.State.ReferenceQuery);
    }
    private sealed class Router : ILearningDictionaryRouter
    {
        public MDictLibraryDescriptor Book { get; } = new(Guid.NewGuid(), "Fixture", "x", [], ".", 1, Guid.NewGuid(), new(LearningLanguage.English));
        public DictionaryConfiguration Configuration { get; private set; }
        public List<DictionarySource> Calls { get; } = [];
        public TaskCompletionSource ResourceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DictionaryResource?> ResourceResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Router() => Configuration = new(enabled: [DictionarySource.Bundled, DictionarySource.System, Book.Source], libraries: [Book], definitionLanguage: LearningLanguage.English);
        public event Action<DictionaryConfiguration>? ConfigurationChanged;
        public int CountWords(string text, LearningLanguage? language) => 1;
        public void Set(DictionaryConfiguration value) { Configuration = value; ConfigurationChanged?.Invoke(value); }
        public Task<LearningDictionaryResult> LookupAsync(string q, LearningLanguage l, bool e, CancellationToken ct = default) => Task.FromResult(LearningDictionaryResult.NotFound);
        public Task<LearningDictionaryResult> LookupSourceAsync(string query, LearningLanguage? language, bool expression, DictionarySource source,
            DictionaryConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Calls.Add(source); if (source == DictionarySource.System) throw new InvalidOperationException();
            return Task.FromResult<LearningDictionaryResult>(source == Book.Source ? new LearningDictionaryResult.Article("<p>bank</p>", "bank", "Fixture", Book.Id) : LearningDictionaryResult.NotFound);
        }
        public Task<DictionaryResource?> ResourceAsync(string path, Guid libraryId, DictionaryConfiguration configuration, CancellationToken cancellationToken = default)
        { ResourceEntered.TrySetResult(); return ResourceResult.Task; }
    }
}
