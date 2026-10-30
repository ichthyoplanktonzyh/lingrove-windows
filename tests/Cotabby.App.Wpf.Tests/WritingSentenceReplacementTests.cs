using Cotabby.App.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

public sealed class WritingSentenceReplacementTests
{
    [Theory]
    [InlineData(RangeEditDispatch.Dispatched, true, "Verified")]
    [InlineData(RangeEditDispatch.Dispatched, false, "Unverified")]
    [InlineData(RangeEditDispatch.Uncertain, true, "Unverified")]
    [InlineData(RangeEditDispatch.Rejected, true, "Rejected")]
    public async Task RangeDispatchNeverFallsBackToBackspacesOrBecomesItsOwnAcknowledgement(RangeEditDispatch result, bool acknowledged, string expected)
    {
        var saved = Field("前你好😀。后", 2) with { SupportsRangeReplacement = true };
        var legacy = new Inserter(); var ranges = new RangeInserter { Result = result };
        await using var owner = new WritingSentenceReplacement(legacy,
            _ => acknowledged ? saved with { Text = "前translated后", CaretOffset = 11 } : saved,
            (_, _) => Task.CompletedTask, ranges);
        Assert.Equal(expected, (await owner.ReplaceAsync(saved, new(1, 5), "你好😀。", "translated", () => true, CancellationToken.None)).ToString());
        Assert.Equal(1, ranges.Calls); Assert.Equal(0, legacy.Calls);
        Assert.False(owner.CanReplace(saved with { SupportsRangeReplacement = false }, new(1, 5), "你好😀。", "translated"));
    }

    [Fact]
    public async Task ShutdownJoinsAnAlreadyAcceptedRangeOperation()
    {
        var saved = Field("你好", 1) with { SupportsRangeReplacement = true };
        var ranges = new RangeInserter { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var owner = new WritingSentenceReplacement(new Inserter(), _ => saved, (_, _) => Task.CompletedTask, ranges);
        var edit = owner.ReplaceAsync(saved, new(0, 2), "你好", "hello", () => true, CancellationToken.None);
        await ranges.Entered.Task; var dispose = owner.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted);
        ranges.Held.SetResult(RangeEditDispatch.Dispatched);
        Assert.Equal(WritingReplacementOutcome.Unverified, await edit); await dispose;
    }

    private sealed class RangeInserter : IRangeTextInserter
    {
        internal int Calls; internal RangeEditDispatch Result;
        internal TaskCompletionSource<RangeEditDispatch>? Held;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RangeEditDispatch> ReplaceRangeAsync(FocusedField target, LearningTextRange range, string source, string replacement,
            Func<bool> permit, CancellationToken token)
        { Calls++; Entered.SetResult(); return Held?.Task ?? Task.FromResult(Result); }
    }
    private static FocusedField Field(string text, int caret) => new() { ProcessId = 22, ProcessName = "fixture", FieldId = "1.2",
        Text = text, CaretOffset = caret, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = false,
        IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false, FocusChangeSequence = 2,
        Composition = InputCompositionState.Clear, CompositionSequence = 1 };
    [Fact]
    public async Task DispatchNeedsExactSameFieldValueAcknowledgement()
    {
        var before = Field("hi there", 8); var inserter = new Inserter();
        await using var owner = new WritingSentenceReplacement(inserter, _ => Field("hi ici", 6), (_, _) => Task.CompletedTask);
        Assert.Equal(WritingReplacementOutcome.Verified, await owner.ReplaceAsync(before, new(3, 5), "there", "ici", () => true, CancellationToken.None));
        Assert.Equal(1, inserter.Calls);
    }
    [Fact]
    public async Task WrongFieldOrUncertainDispatchCannotBecomeSuccess()
    {
        var before = Field("hello", 5); var inserter = new Inserter();
        await using var owner = new WritingSentenceReplacement(inserter, _ => Field("bonjour", 7) with { FocusChangeSequence = 3 }, (_, _) => Task.CompletedTask);
        Assert.Equal(WritingReplacementOutcome.Unverified, await owner.ReplaceAsync(before, new(0, 5), "hello", "bonjour", () => true, CancellationToken.None));
        Assert.Equal(1, inserter.Calls);
        Assert.Equal(WritingReplacementOutcome.Rejected, await owner.ReplaceAsync(before, new(0, 4), "hell", "x", () => true, CancellationToken.None));
        Assert.Equal(1, inserter.Calls);
    }
    [Fact]
    public async Task ShutdownWaitsForAnAlreadyAcceptedNoncooperativeDispatch()
    {
        var before = Field("hello", 5); var inserter = new Inserter { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var owner = new WritingSentenceReplacement(inserter, _ => Field("bonjour", 7), (_, _) => Task.CompletedTask);
        var work = owner.ReplaceAsync(before, new(0, 5), "hello", "bonjour", () => true, CancellationToken.None);
        await inserter.Entered.Task; var dispose = owner.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted);
        inserter.Held.SetResult(true); Assert.Equal(WritingReplacementOutcome.Unverified, await work); await dispose;
    }
    private sealed class Inserter : IConditionalTextInserter
    {
        public int Calls; public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? Held;
        public Task<bool> ReplaceWithPermitAsync(FocusedField target, int count, string text, Func<bool> permit, CancellationToken ct)
        { Calls++; Entered.TrySetResult(); return Held?.Task ?? Task.FromResult(permit()); }
    }
}
