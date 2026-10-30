using Cotabby.App.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

public sealed class WritingSentenceReplacementTests
{
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
