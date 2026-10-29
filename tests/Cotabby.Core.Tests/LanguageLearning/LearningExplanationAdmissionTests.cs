using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExplanationAdmissionTests
{
    [Fact]
    public async Task RefreshWorkerScheduledLateCannotReserveAfterNewerRefresh()
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        CancellationTokenSource? current = null; int reservations = 0, factories = 0;
        ILearningExplanationAdmission Reserve()
        {
            current?.Cancel(); current = new CancellationTokenSource();
            return new Admission(current, Interlocked.Increment(ref reservations) == 1 ? () => { blocked.TrySetResult(); release.Wait(); } : null,
                () => Interlocked.Increment(ref factories));
        }
        await using var service = new LearningExplanationService(new() { LocalModel = "model.gguf" }, LearningLanguage.English,
            _ => throw new InvalidOperationException(), reserveAdmission: Reserve);
        var input = new LearningExplanationInput("know", "know", LearningLanguage.English, VocabularyEntryKind.Word, "Let me know.", [new(7, 4)]);
        var older = service.GenerateAsync(input, "old", refresh: true);
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var newer = service.GenerateAsync(input, "new", refresh: true);
            var result = await newer.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => older.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("new", result.Result.Meaning); Assert.Equal(2, reservations); Assert.Equal(1, factories);
        }
        finally { release.Set(); }
    }

    private sealed class Admission(CancellationTokenSource source, Action? wait, Action factory) : ILearningExplanationAdmission
    {
        public CancellationToken Token { get { wait?.Invoke(); return source.Token; } }
        public ILearningExplanationEngine CreateEngine(LearningModelSnapshot profile) { source.Token.ThrowIfCancellationRequested(); factory(); return new Engine(); }
        public void Dispose() => source.Dispose();
    }
    private sealed class Engine : ILearningExplanationEngine
    {
        public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage language, int output, CancellationToken token) =>
            Task.FromResult(new LearningExplanationResult("new", "explanation"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
