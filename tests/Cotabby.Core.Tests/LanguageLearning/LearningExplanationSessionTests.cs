using System.Threading.Channels;
using System.Runtime.CompilerServices;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Ports Mac session regressions and stresses delayed effects without a runtime or UI.</summary>
public sealed class LearningExplanationSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static LearningExplanationInput Input(string text = "Sentence.") => new(text, text, LearningLanguage.English,
        VocabularyEntryKind.Sentence, text, [new(0, text.Length)]);
    private static LearningExplanationRecord Record(LearningExplanationInput input, LearningLanguage language = LearningLanguage.Chinese) =>
        new(Guid.NewGuid(), input, language, new("Meaning", "Explanation"), "fixture", DateTimeOffset.UtcNow);

    [Fact]
    public async Task NavigationNeverGeneratesAndLateRepliesCannotEnterTheNextTarget()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input("First.")); Assert.False(session.State.IsGenerating); Assert.Equal(0, service.Generations);
        var firstWork = session.ExplainAsync(); var first = await service.Next();
        session.SetTarget(Input("Second.")); var secondWork = session.ExplainAsync(); var second = await service.Next();
        try
        {
            Assert.True(first.Token.IsCancellationRequested);
            first.Complete(); await firstWork.WaitAsync(Timeout);
            Assert.Null(session.State.Record); Assert.True(session.State.IsGenerating);
            second.Complete(); await secondWork.WaitAsync(Timeout);
            Assert.Equal(Input("Second."), session.State.Record!.Input); Assert.Equal(1, service.Displays);
        }
        finally { first.Complete(); second.Complete(); }
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("preferences")]
    [InlineData("clear")]
    public async Task RetirementRejectsUncooperativeRepliesWithoutShowingAnError(string action)
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input()); var work = session.ExplainAsync(); var call = await service.Next();
        try
        {
            if (action == "stop") session.Cancel();
            else if (action == "clear") session.Clear();
            else service.ChangeLanguage(LearningLanguage.French);
            Assert.True(call.Token.IsCancellationRequested); call.Complete(); await work.WaitAsync(Timeout);
            Assert.False(session.State.IsGenerating); Assert.Null(session.State.Record); Assert.Null(session.State.ErrorMessage);
            Assert.Equal(action == "clear" ? null : Input(), session.State.Input); Assert.Equal(0, service.Displays);
        }
        finally { call.Complete(); }
    }

    [Fact]
    public async Task RefreshKeepsThePreviousResultThroughStopAndFailureButNewTargetClearsIt()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        var saved = Record(Input()); session.SetTarget(Input(), saved);
        await session.ExplainAsync(); Assert.Equal(0, service.Generations);
        var stopped = session.ExplainAsync(refresh: true); var first = await service.Next();
        try
        {
            Assert.True(session.State.IsGenerating); Assert.Equal(saved, session.State.Record);
            session.Cancel(); first.Complete(); await stopped.WaitAsync(Timeout);
            Assert.Equal(saved, session.State.Record); Assert.Null(session.State.ErrorMessage);
        }
        finally { first.Complete(); }
        var failed = session.ExplainAsync(refresh: true); var second = await service.Next();
        try
        {
            Assert.True(second.Refresh); Assert.StartsWith("req_", second.RequestId);
            second.Completion.TrySetException(new IOException("private endpoint or model path")); await failed.WaitAsync(Timeout);
            Assert.False(session.State.IsGenerating); Assert.Equal(saved, session.State.Record);
            Assert.Equal(new LearningExplanationException(LearningExplanationFailure.ProviderFailure).Message, session.State.ErrorMessage);
            session.SetTarget(Input("Different.")); Assert.Null(session.State.Record); Assert.Null(session.State.ErrorMessage);
        }
        finally { second.Complete(); }
    }

    [Fact]
    public async Task RestoringSavedRequiresTheSameOccurrenceAndCurrentResponseLanguage()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input(), Record(Input(), LearningLanguage.English)); Assert.Null(session.State.Record);
        var matching = Record(Input()); session.SetTarget(Input(), matching); Assert.Equal(matching, session.State.Record);
        session.Clear(); session.SetTarget(Input(), Record(Input("Another."))); Assert.Null(session.State.Record);
        Assert.Equal(0, service.Generations);
    }

    [Fact]
    public async Task CanonicalEqualityCannotAuthorizeInvalidSavedUtf16Ranges()
    {
        var requested = new LearningExplanationInput("é", "é", LearningLanguage.French, VocabularyEntryKind.Word, "é", [new(0, 1)]);
        var malformed = new LearningExplanationInput("e\u0301", "e\u0301", LearningLanguage.French, VocabularyEntryKind.Word, "e\u0301", [new(0, 1)]);
        Assert.Equal(requested, malformed); Assert.False(LearningExplanationPolicy.IsValid(malformed));
        await using var session = new LearningExplanationSession();
        session.SetTarget(requested, Record(malformed)); Assert.Null(session.State.Record);
    }

    [Fact]
    public async Task AsyncSavedRestorationPublishesWithoutGeneratingOrRecordingDisplay()
    {
        var saved = Record(Input());
        var service = new Service { Load = _ => Task.FromResult<LearningExplanationRecord?>(saved) };
        var states = Channel.CreateUnbounded<LearningExplanationState>();
        await using var session = new LearningExplanationSession(service);
        session.StateChanged += state => states.Writer.TryWrite(state);
        session.SetTarget(Input());
        LearningExplanationState observed;
        do { observed = await states.Reader.ReadAsync().AsTask().WaitAsync(Timeout); } while (observed.Record is null);
        Assert.Equal(saved, observed.Record); Assert.False(observed.IsGenerating);
        Assert.Equal(0, service.Generations); Assert.Equal(0, service.Displays);
    }

    [Fact]
    public async Task DuplicateExplainDoesNotSupersedeAnActiveRequest()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input()); var work = session.ExplainAsync(); var call = await service.Next();
        try
        {
            await session.ExplainAsync(refresh: true);
            Assert.Equal(1, service.Generations); Assert.False(call.Token.IsCancellationRequested);
            call.Complete(); await work.WaitAsync(Timeout); Assert.Equal(call.Record, session.State.Record);
        }
        finally { call.Complete(); }
    }

    [Fact]
    public async Task PendingSavedLookupDoesNotHoldTeardownAfterANewerGeneratedRecord()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service { Load = _ => { lookupStarted.TrySetResult(); return saved.Task; } };
        await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input());
        try
        {
            await lookupStarted.Task.WaitAsync(Timeout);
            var work = session.ExplainAsync(); var call = await service.Next();
            try
            {
                call.Complete(); await work.WaitAsync(Timeout); var generated = session.State.Record;
                Assert.False(saved.Task.IsCompleted);
                var closing = session.DisposeAsync().AsTask(); Assert.True(closing.IsCompleted);
                await closing.WaitAsync(Timeout); saved.SetResult(Record(Input()));
                Assert.NotNull(generated); Assert.Equal(1, service.Displays);
                Assert.Null(session.State.Record); Assert.Null(session.State.Input);
            }
            finally { call.Complete(); }
        }
        finally { saved.TrySetResult(null); }
    }

    [Fact]
    public async Task ReturningToAnEqualTargetStillRejectsItsEarlierRevision()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input("A.")); var oldWork = session.ExplainAsync(); var old = await service.Next();
        session.SetTarget(Input("B.")); session.SetTarget(Input("A."));
        var currentWork = session.ExplainAsync(); var current = await service.Next();
        try
        {
            old.Complete(); await oldWork.WaitAsync(Timeout); Assert.Null(session.State.Record); Assert.True(session.State.IsGenerating);
            current.Complete(); await currentWork.WaitAsync(Timeout); Assert.Equal(current.Record, session.State.Record);
        }
        finally { old.Complete(); current.Complete(); }
    }

    [Fact]
    public async Task PendingSavedLookupDoesNotHoldTeardownAfterPreferenceChanges()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service { Load = _ => { loaded.TrySetResult(); return pending.Task; } };
        await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input());
        try
        {
            await loaded.Task.WaitAsync(Timeout);
            service.ChangeLanguage(LearningLanguage.French);
            Assert.False(pending.Task.IsCompleted);
            var closing = session.DisposeAsync().AsTask(); Assert.True(closing.IsCompleted);
            await closing.WaitAsync(Timeout); pending.SetResult(Record(Input(), LearningLanguage.Chinese));
            Assert.Null(session.State.Record); Assert.Equal(0, service.Generations);
        }
        finally { pending.TrySetResult(null); }
    }

    [Fact]
    public async Task ReentrantSavedCallbackCannotRestoreAfterClear()
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        service.Saved = input => { session.Clear(); return Record(input); };
        session.SetTarget(Input()); Assert.Null(session.State.Input); Assert.Null(session.State.Record);
        Assert.Equal(0, service.Generations);
    }

    [Theory]
    [InlineData("wrong-input")]
    [InlineData("wrong-language")]
    [InlineData("invalid-result")]
    public async Task MalformedGeneratedRecordsBecomeFixedFailures(string failure)
    {
        var service = new Service(); await using var session = new LearningExplanationSession(service);
        session.SetTarget(Input()); var work = session.ExplainAsync(); var call = await service.Next();
        try
        {
            var record = Record(Input());
            record = failure switch
            {
                "wrong-input" => record with { Input = Input("Other.") },
                "wrong-language" => record with { Language = LearningLanguage.French },
                _ => record with { Result = new("", "") },
            };
            call.Completion.TrySetResult(record); await work.WaitAsync(Timeout);
            Assert.Null(session.State.Record); Assert.False(session.State.IsGenerating); Assert.Equal(0, service.Displays);
            Assert.Equal(new LearningExplanationException(LearningExplanationFailure.InvalidContent).Message, session.State.ErrorMessage);
        }
        finally { call.Complete(); }
    }

    [Fact]
    public async Task AbsentServiceAndInvalidTargetsNeverStartWorkButOfflineSavedRecordsCanBeViewed()
    {
        await using var session = new LearningExplanationSession(); Assert.False(session.IsConfigured);
        session.SetTarget(new("", "", LearningLanguage.English, VocabularyEntryKind.Word, "", []));
        await session.ExplainAsync(); Assert.Null(session.State.Input);
        var saved = Record(Input(), LearningLanguage.French); session.SetTarget(Input(), saved);
        Assert.Equal(saved, session.State.Record); await session.ExplainAsync(); Assert.False(session.State.IsGenerating);
    }

    [Fact]
    public async Task DisposeUnsubscribesImmediatelyWithoutWaitingForBorrowedWork()
    {
        var service = new Service(); var session = new LearningExplanationSession(service);
        Assert.Equal(1, service.Subscribers);
        session.SetTarget(Input()); var work = session.ExplainAsync(); var call = await service.Next();
        try
        {
            var disposal = session.DisposeAsync().AsTask();
            Assert.True(call.Token.IsCancellationRequested); Assert.Equal(0, service.Subscribers); Assert.True(disposal.IsCompleted);
            call.Complete(); await disposal.WaitAsync(Timeout); await work.WaitAsync(Timeout); await session.DisposeAsync();
            Assert.Null(session.State.Input); Assert.Null(session.State.Record); Assert.True(service.IsConfigured);
            session.SetTarget(Input()); await session.ExplainAsync(); Assert.Equal(1, service.Generations);
        }
        finally { call.Complete(); await session.DisposeAsync().AsTask().WaitAsync(Timeout); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncooperativeBorrowedWorkDoesNotRootTheDisposedSessionOrView(bool lookup)
    {
        var service = new Service(); var pending = new TaskCompletionSource<LearningExplanationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = CreateRetiredSession(service, pending, lookup);
        try
        {
            // NoInlining keeps the only strong session/view locals on a completed helper stack,
            // rather than letting JIT liveness in this test accidentally root the objects.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(retired.Session.IsAlive, "Session was retained by suspended work.");
            Assert.False(retired.View.IsAlive, "View subscriber was retained after disposal.");
            Assert.Equal(0, service.Subscribers);
            Assert.False(lookup ? pending.Task.IsCompleted : retired.Call!.Completion.Task.IsCompleted);
        }
        finally
        {
            pending.TrySetResult(null); retired.Call?.Complete();
            if (retired.Work is not null) await retired.Work.WaitAsync(Timeout);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Session, WeakReference View, Task? Work, Call? Call) CreateRetiredSession(
        Service service, TaskCompletionSource<LearningExplanationRecord?> pending, bool lookup)
    {
        using var entered = new ManualResetEventSlim();
        if (lookup) service.Load = _ => { entered.Set(); return pending.Task; };
        var session = new LearningExplanationSession(service); var view = new ViewProbe();
        // A method target avoids a shared compiler closure that would itself let service.Load
        // retain the view, independently of the production session under test.
        session.StateChanged += view.Observe;
        session.SetTarget(Input());
        Task? work = null; Call? call = null;
        if (lookup) Assert.True(entered.Wait(Timeout));
        else { work = session.ExplainAsync(); call = service.Next().GetAwaiter().GetResult(); }
        session.DisposeAsync().GetAwaiter().GetResult();
        return (new(session), new(view), work, call);
    }

    private sealed class ViewProbe { public void Observe(LearningExplanationState state) { } }

    private sealed record Call(LearningExplanationInput Input, LearningLanguage Language, CancellationToken Token, string RequestId, bool Refresh)
    {
        public TaskCompletionSource<LearningExplanationRecord> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LearningExplanationRecord Record { get; } = LearningExplanationSessionTests.Record(Input, Language);
        public void Complete() => Completion.TrySetResult(Record);
    }
    private sealed class Service : ILearningExplanationGenerating
    {
        public event Action? Changed;
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public LearningLanguage Language { get; private set; } = LearningLanguage.Chinese;
        public bool IsConfigured => true;
        public int Generations, Displays;
        public Func<LearningExplanationInput, LearningExplanationRecord?>? Saved;
        public Func<LearningExplanationInput, Task<LearningExplanationRecord?>>? Load;
        private readonly Channel<Call> _calls = Channel.CreateUnbounded<Call>();
        public Task<Call> Next() => _calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Generations); var call = new Call(input, Language, cancellationToken, requestId, refresh);
            _calls.Writer.TryWrite(call); return call.Completion.Task;
        }
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Load?.Invoke(input) ?? Task.FromResult<LearningExplanationRecord?>(null);
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => Saved?.Invoke(input);
        public void RecordDisplay(string requestId) => Interlocked.Increment(ref Displays);
        public void ChangeLanguage(LearningLanguage language) { Language = language; Changed?.Invoke(); }
    }
}
