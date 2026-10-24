namespace Cotabby.Core.Tests.Suggestions;

/// <summary>The queued-key contract depends on a monotonic 800ms ownership window. A gated
/// continuation and pump barriers prove the second key is actually queued, while virtual time
/// separates the policy boundary from CI scheduling/cold-start latency. Production time is intact.</summary>
public sealed class SuggestionQueuedAcceptanceTests : CoordinatorHarness
{
    private readonly OwnershipClock _clock;
    public SuggestionQueuedAcceptanceTests() : this(new OwnershipClock()) { }
    private SuggestionQueuedAcceptanceTests(OwnershipClock clock) : base(clock: clock) { _clock = clock; }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedAcceptIsAppliedOnlyBeforeTheOwnershipWindowExpires(bool expired)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.GenerateOverride = async (request, cancellationToken) =>
        {
            if (request.Prefix.EndsWith("hello", StringComparison.Ordinal)) return " world";
            if (!request.Prefix.EndsWith("hello world", StringComparison.Ordinal)) return "";
            entered.TrySetResult(); return await release.Task.WaitAsync(cancellationToken);
        };
        try
        {
            Type("hello"); await Shown(" world");
            Assert.True(Hook.Press(Keys.Tab()));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await RunOnUiAsync(() =>
            {
                Assert.Equal(new[] { " world" }, Focus.InsertionSnapshot);
                _clock.Advance(700);
            });
            Assert.True(Hook.Press(Keys.Tab()), "the producer-thread key is owned while continuation generation is held");
            // The hook posts the queued epoch. This second pump barrier runs after that post;
            // releasing the engine earlier would also permit an ordinary visible-text accept.
            await RunOnUiAsync(() => _clock.Advance(expired ? 100 : 99));
            release.TrySetResult(" again soon");
            if (expired)
            {
                await Shown(" again soon");
                await RunOnUiAsync(() => Assert.Equal(new[] { " world" }, Focus.InsertionSnapshot));
            }
            else
            {
                await Shown(" soon");
                // Overlay publication precedes the asynchronous host insertion. Observe its
                // completion separately through the fake's locked snapshot.
                await Eventually(() => Focus.InsertionSnapshot.SequenceEqual([" world", " again"]), "the queued host insertion completes");
            }
        }
        finally { release.TrySetResult(" again soon"); }
    }
    private sealed class OwnershipClock : TimeProvider
    {
        private readonly DateTimeOffset _origin = DateTimeOffset.UtcNow;
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);
        public override DateTimeOffset GetUtcNow() => _origin.AddMilliseconds(GetTimestamp());
        internal void Advance(long milliseconds) => Interlocked.Add(ref _milliseconds, milliseconds);
    }
}
