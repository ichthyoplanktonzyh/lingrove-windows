using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExplanationRuntimeIdentityTests
{
    [Theory]
    [InlineData("sampling", true)]
    [InlineData("output", true)]
    [InlineData("style", true)]
    [InlineData("model", false)]
    [InlineData("context", false)]
    [InlineData("template", false)]
    public void MacAllocationIdentityExcludesOnlyRequestParameters(string change, bool same)
    {
        var first = new LearningModelSnapshot { LocalModel = "tutor.gguf" };
        var next = change switch
        {
            "sampling" => first with { SamplingOverrides = new(Temperature: 0.5) },
            "output" => first with { MaximumOutputTokens = 768 },
            "style" => first with { TranslationStyle = new(LearningTranslationRegister.Formal) },
            "model" => first with { LocalModel = "other.gguf" },
            "context" => first with { ContextWindowTokens = 16384 },
            _ => first with { Template = LearningChatTemplate.Qwen3 },
        };
        Assert.Equal(same, first.UsesSameRuntime(next)); Assert.Equal(same, next.UsesSameRuntime(first));
    }

    [Fact]
    public async Task OwnerRetirementPreservesCompleteCacheButRejectsUncooperativePendingWork()
    {
        var pending = new TaskCompletionSource<LearningExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var service = new LearningExplanationService(new() { LocalModel = "tutor.gguf" }, LearningLanguage.English,
            _ => new Engine(() => ++calls == 1 ? Task.FromResult(new LearningExplanationResult("Meaning", "Explanation")) : Wait()));
        Task<LearningExplanationResult> Wait() { started.SetResult(); return pending.Task; }
        var input = new LearningExplanationInput("word", "word", LearningLanguage.English, VocabularyEntryKind.Word, "word", [new(0, 4)]);
        var saved = await service.GenerateAsync(input, "req_first").WaitAsync(TimeSpan.FromSeconds(10));
        var work = service.GenerateAsync(input, "req_pending", refresh: true);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            service.CancelPending(); pending.SetResult(new("Late", "Late explanation"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(saved, service.Cached(input)); Assert.True(service.IsConfigured);
        }
        finally { pending.TrySetResult(new("Cleanup", "Cleanup explanation")); }
    }

    private sealed class Engine(Func<Task<LearningExplanationResult>> generate) : ILearningExplanationEngine
    {
        public Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage, int maximumOutputTokens, CancellationToken ct) => generate();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
