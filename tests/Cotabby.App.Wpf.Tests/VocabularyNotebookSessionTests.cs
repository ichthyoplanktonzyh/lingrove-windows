using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;
using static Cotabby.Tests.Shared.VocabularyReferenceGraph;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Executes the production dispatcher-owned notebook and its real Core child sessions. Expected
/// states come from the complete pinned Swift sessions; only generator, successful dictionary
/// provider and native word facts are substituted. No window, model or network is involved.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyNotebookSessionTests(WpfDispatcher dispatcher)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-notebook-session-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public Task PinnedMacOwnsIdleNavigationRowReuseCancellationAndExplicitDictionaryQueries(string id) => dispatcher.RunAsync(async () =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyNotebookSession.swift", row.GetProperty("macSource").GetString());
        string mode = row.GetProperty("operation").GetString()!;
        var entry = Entry(); var sense = entry.Senses[0];
        if (mode == "no-standalone") entry = WithFirst(entry, sense with { IsSavedWithoutContext = false });
        if (mode == "standalone") entry = WithFirst(entry, sense with { Examples = [] });
        if (mode == "dictionary-empty-form") entry = entry with { DictionaryForm = "" };
        if (mode == "dictionary-pattern") entry = WithFirst(Copy(entry, kind: VocabularyEntryKind.Pattern) with { Expression = "keep me posted", DictionaryForm = "keep someone posted" }, sense with { Examples = [] });
        if (mode == "dictionary-canonical") entry = entry with { DictionaryForm = "café" };
        var provider = new Provider(); var service = new Service(); var text = new TextProbe(row.GetProperty("facts"));
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text, service);
        if (mode is "saved" or "wrong-language")
        {
            var input = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, entry.Senses[0].Examples[0], entry.Senses[0]));
            var english = Record(input, LearningLanguage.English, "saved", 201); var wrong = Record(input, LearningLanguage.Chinese, "wrong", 202);
            entry = WithFirst(entry, entry.Senses[0] with { Examples = [entry.Senses[0].Examples[0] with { Explanations = mode == "saved" ? [english, wrong] : [wrong] }] });
        }
        Task? earlier = null; bool? reused = null, priorStopped = null;
        try
        {
            session.Select(entry, mode == "missing" ? Id(999) : mode == "alias" ? Id(99) : Id(2));
            session.Explanations.TryGetValue(Key(3), out var first);
            if (mode is "second" or "remove" or "context" or "note")
            { earlier = first!.ExplainAsync(); await service.Next(); }
            switch (mode)
            {
                case "second": session.Select(entry, Id(4)); priorStopped = !first!.State.IsGenerating; break;
                case "same": session.Select(entry, Id(2)); reused = ReferenceEquals(first, session.Explanations[Key(3)]); break;
                case "note": entry = WithFirst(entry, entry.Senses[0] with { Note = "changed" }); session.Select(entry, Id(2)); reused = ReferenceEquals(first, session.Explanations[Key(3)]); break;
                case "context":
                    entry = WithFirst(entry, entry.Senses[0] with { Examples = [entry.Senses[0].Examples[0] with { Text = "The bank opens.", Translation = "changed annotation" }] });
                    session.Select(entry, Id(2)); reused = ReferenceEquals(first, session.Explanations[Key(3)]); break;
                case "remove": entry = WithFirst(entry, entry.Senses[0] with { Examples = [] }); session.Select(entry, Id(2)); priorStopped = !first!.State.IsGenerating; break;
                case "nil": session.Select(null); break;
                case "activate":
                    var retired = first!.ExplainAsync(); await service.Next(); session.ActivateExplanation("standalone"); priorStopped = !first.State.IsGenerating;
                    var active = session.Explanations["standalone"].ExplainAsync(); await service.Next(); service.Release(); await Task.WhenAll(retired, active).WaitAsync(Timeout); break;
                case "cancel":
                    var stopping = first!.ExplainAsync(); await service.Next(); session.Cancel(); service.Release(); await stopping.WaitAsync(Timeout); priorStopped = !first.State.IsGenerating; break;
            }
            if (mode is "second" or "remove" or "context") { service.Release(); await earlier!.WaitAsync(Timeout); }
            if (mode.StartsWith("dictionary-", StringComparison.Ordinal))
            {
                await session.ShowDictionaryAsync(entry).WaitAsync(Timeout);
                switch (mode)
                {
                    case "dictionary-repeat": await session.ShowDictionaryAsync(entry); break;
                    case "dictionary-hide": session.HideDictionary(); break;
                    case "dictionary-resume": session.Cancel(); await session.Resume().WaitAsync(Timeout); break;
                    case "dictionary-form": entry = entry with { DictionaryForm = "riverbank" }; session.Select(entry, Id(2)); await session.ShowDictionaryAsync(entry).WaitAsync(Timeout); break;
                    case "dictionary-language": entry = Copy(entry, language: LearningLanguage.French); session.Select(entry, Id(2)); break;
                    case "dictionary-canonical": entry = entry with { DictionaryForm = "cafe\u0301" }; session.Select(entry, Id(2)); await session.ShowDictionaryAsync(entry); break;
                }
            }
            var state = session.Dictionary.State; var unit = state.Unit;
            var actual = JsonSerializer.SerializeToNode(new
            {
                entry = session.EntryId?.ToString("D"), sense = session.SenseId?.ToString("D"),
                rows = session.Explanations.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
                { key = pair.Key, input = InputJson(pair.Value.State.Input), meaning = pair.Value.State.Record?.Result.Meaning, generating = pair.Value.State.IsGenerating }),
                dictionary = new { open = unit is not null, query = state.Query, unit = unit is null ? null : new
                { id = unit.Id, surface = unit.Surface, query = unit.Query, kind = unit.Kind.ToString().ToLowerInvariant(), ranges = unit.Ranges.Select(range => new[] { range.Location, range.Length }),
                    words = unit.Words.Select(word => new { text = word.Text, location = word.Range.Location, length = word.Range.Length }) } },
                dictionaryCalls = provider.Calls, dictionaryRequests = provider.Requests.Select(request => new { query = request.Query, language = request.Language.Code(), expression = request.Expression }),
                generations = service.Generations, displays = service.Displays, reused, priorStopped,
            });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
        }
        finally { session.Cancel(); service.Release(); if (earlier is not null) await earlier.WaitAsync(Timeout); }
    });

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("removed")]
    public Task SelectionRevokesOldWorkBeforeAnyForeignSavedCallbackCanReleaseItOrRestartIt(string change) => dispatcher.RunAsync(async () =>
    {
        var entry = Entry(); var service = new Service();
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new TextProbe(), service);
        session.Select(entry, Id(2)); var old = session.Explanations[Key(3)]; var work = old.ExplainAsync(); await service.Next(); var oldState = old.State;
        var changed = change == "owner" ? entry : WithFirst(entry, entry.Senses[0] with
        { Examples = change == "removed" ? [] : [entry.Senses[0].Examples[0] with { Text = "The bank opens." }] });
        bool observed = false; Exception? callbackFailure = null;
        service.Saved = input =>
        {
            if (observed) return null;
            observed = true;
            try
            {
                service.Change(); service.Release(); work.WaitAsync(Timeout).GetAwaiter().GetResult();
                // A reused child already has its new input here, so the original view's snapshot
                // also guards admission. Removed/changed-owner children remain fully suspended.
                old.ExplainAsync(expectedState: change == "input" ? oldState : null).WaitAsync(Timeout).GetAwaiter().GetResult();
                Assert.Null(old.State.Record); Assert.Equal(1, service.Generations); Assert.Equal(0, service.Displays);
            }
            catch (Exception error) { callbackFailure = error; } // SavedRecord failures are intentionally swallowed by production.
            return null;
        };
        try
        {
            session.Select(changed, change == "owner" ? Id(4) : Id(2));
            Assert.True(observed); Assert.Null(callbackFailure); Assert.Equal(0, service.Displays); Assert.Equal(session.Explanations.Count, service.Subscribers);
        }
        finally { service.Saved = null; service.Release(); await work.WaitAsync(Timeout); }
    });

    [Fact]
    public Task TheSameQueryInAnotherEntryReceivesItsOwnDocumentIdentityAndCannotOpenThroughAStaleCaller() => dispatcher.RunAsync(async () =>
    {
        var provider = new Provider(); var first = Entry(); var next = Copy(first, id: Id(8));
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new TextProbe());
        session.Select(first, Id(2)); await session.ShowDictionaryAsync(first).WaitAsync(Timeout);
        Assert.Equal(Key(1), session.Dictionary.State.DocumentId);
        session.Select(next, Id(2)); Assert.Null(session.Dictionary.State.Unit);
        await session.ShowDictionaryAsync(first); Assert.Null(session.Dictionary.State.Unit); Assert.Equal(1, provider.Calls);
        await session.ShowDictionaryAsync(next).WaitAsync(Timeout);
        Assert.Equal(Key(8), session.Dictionary.State.DocumentId); Assert.Equal("bank", session.Dictionary.State.Query); Assert.Equal(2, provider.Calls);
    });

    [Fact]
    public Task ReentrantNotificationsDuringWordPreparationAndOpenDeduplicateTheSameExplicitQuery() => dispatcher.RunAsync(async () =>
    {
        var text = new TextProbe(); var provider = new Provider(); var service = new Service(); var entry = Entry();
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text, service);
        session.Select(entry, Id(2)); int notifications = 0;
        session.Changed += () => { notifications++; _ = session.ShowDictionaryAsync(entry); };
        await session.ShowDictionaryAsync(entry).WaitAsync(Timeout);
        Assert.InRange(notifications, 2, 3); Assert.Equal(1, text.Calls); Assert.Equal(1, provider.Calls); Assert.Equal(0, service.Generations);
    });

    [Fact]
    public Task ASlowNativeWordPreparationCannotReplaceANewerEntryAndDoesNotBlockDisposal() => dispatcher.RunAsync(async () =>
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var text = new TextProbe { Prepare = (query, language) => { if (query == "bank") { entered.Set(); Assert.True(release.Wait(Timeout)); } return []; } };
        var provider = new Provider(); var entry = Entry(); var next = Copy(entry, id: Id(8)) with { Expression = "coffee", DictionaryForm = "coffee" };
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text);
        session.Select(entry, Id(2)); var old = session.ShowDictionaryAsync(entry);
        try
        {
            Assert.True(entered.Wait(Timeout)); session.Select(next, Id(2)); await session.ShowDictionaryAsync(next).WaitAsync(Timeout);
            Assert.Equal("coffee", session.Dictionary.State.Query); Assert.Equal(1, provider.Calls);
            await session.DisposeAsync(); Assert.False(old.IsCompleted); release.Set(); await old.WaitAsync(Timeout);
            Assert.Null(session.Dictionary.State.Unit); Assert.Equal(1, provider.Calls);
        }
        finally { release.Set(); await old.WaitAsync(Timeout); await session.DisposeAsync(); }
    });

    [Fact]
    public Task NativePreparationFailureIsFixedAndAnExplicitRetryCanSucceedWithoutStartingAi() => dispatcher.RunAsync(async () =>
    {
        var text = new TextProbe { Prepare = (_, _) => throw new IOException("private query path") }; var provider = new Provider(); var service = new Service(); var entry = Entry();
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text, service);
        session.Select(entry, Id(2)); await session.ShowDictionaryAsync(entry).WaitAsync(Timeout);
        Assert.Equal("Lexical text processing is unavailable on this system.", session.DictionaryError); Assert.Null(session.Dictionary.State.Unit); Assert.Equal(0, provider.Calls);
        text.Prepare = (_, _) => []; await session.ShowDictionaryAsync(entry).WaitAsync(Timeout);
        Assert.Null(session.DictionaryError); Assert.Equal("bank", session.Dictionary.State.Query); Assert.Equal(1, provider.Calls); Assert.Equal(0, service.Generations);
    });

    [Fact]
    public Task CancelKeepsOnlyTheRememberedExplicitDictionaryAndHidePreventsResumeWhileLateProvidersAreRejected() => dispatcher.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<LearningDictionaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Lookup = _ => { entered.TrySetResult(); return pending.Task; } }; var entry = Entry(); var service = new Service();
        await using var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new TextProbe(), service);
        session.Select(entry, Id(2)); var first = session.ShowDictionaryAsync(entry); await entered.Task.WaitAsync(Timeout);
        try
        {
            session.Cancel(); Assert.Null(session.Dictionary.State.Unit);
            provider.Lookup = request => Task.FromResult<LearningDictionaryResult>(Result(request.Query, request.Language));
            await session.Resume().WaitAsync(Timeout); Assert.Equal(2, provider.Calls); var accepted = session.Dictionary.State;
            pending.TrySetResult(Result("late private headword", LearningLanguage.English)); await first.WaitAsync(Timeout);
            Assert.Same(accepted, session.Dictionary.State);
            session.HideDictionary(); await session.Resume(); Assert.Equal(2, provider.Calls); Assert.Null(session.Dictionary.State.Unit); Assert.Equal(0, service.Generations);
            await session.DisposeAsync(); Assert.Equal(0, service.Subscribers);
        }
        finally { pending.TrySetResult(Result("late", LearningLanguage.English)); await first.WaitAsync(Timeout); }
    });

    private static VocabularyEntry Entry()
    {
        var entry = Document().Entries[0]; var second = new VocabularyExample(Id(6), "My bank.", "", [new(3, 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch);
        return entry with { Senses = [entry.Senses[0] with { MergedFrom = [Id(99)] }, new(Id(4), "other", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Examples = [second] }] };
    }
    private static VocabularyEntry WithFirst(VocabularyEntry entry, VocabularySense sense) => entry with { Senses = [sense, .. entry.Senses.Skip(1)] };
    private static VocabularyEntry Copy(VocabularyEntry entry, Guid? id = null, LearningLanguage? language = null, VocabularyEntryKind? kind = null) =>
        new(id ?? entry.Id, entry.Expression, entry.DictionaryForm, language ?? entry.Language, kind ?? entry.Kind, entry.Senses, entry.CreatedAt, entry.UpdatedAt);
    private static string Key(int n) => Id(n).ToString("D").ToUpperInvariant();
    private static LearningExplanationRecord Record(LearningExplanationInput input, LearningLanguage language, string meaning = "generated", int id = 200) =>
        new(Id(id), input, language, new(meaning, "prose"), "fixture", DateTimeOffset.UnixEpoch);
    private static JsonNode? InputJson(LearningExplanationInput? input) => input is null ? null
        : JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Explanation(Record(input, LearningLanguage.English))))!["input"]!.DeepClone();
    private static LearningDictionaryResult Result(string query, LearningLanguage language) => new LearningDictionaryResult.Entries(
        [new("fixture", language.Code(), query, [], [], "")]);
    private sealed record Request(string Query, LearningLanguage Language, bool Expression);
    private sealed class Provider : ILearningDictionaryProviding
    {
        private readonly ConcurrentQueue<Request> _requests = new();
        public Request[] Requests => _requests.ToArray(); public int Calls => _requests.Count;
        public Func<Request, Task<LearningDictionaryResult>>? Lookup;
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default)
        { var request = new Request(query, language, expression); _requests.Enqueue(request); return Lookup?.Invoke(request) ?? Task.FromResult(Result(query, language)); }
    }
    private sealed class TextProbe : ILearningLexicalText
    {
        private readonly JsonElement? _facts;
        public int Calls;
        public Func<string, LearningLanguage, IReadOnlyList<LearningDictionaryWord>>? Prepare;
        public TextProbe() { }
        public TextProbe(JsonElement facts) => _facts = facts;
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language)
        {
            Interlocked.Increment(ref Calls); if (Prepare is { } prepare) return prepare(value, language);
            if (_facts is not { } facts) return [];
            var row = facts.EnumerateArray().First(item => item.GetProperty("query").GetString() == value && item.GetProperty("language").GetString() == language.Code());
            return row.GetProperty("words").EnumerateArray().Select(word => new LearningDictionaryWord(word.GetProperty("text").GetString()!,
                new(word.GetProperty("location").GetInt32(), word.GetProperty("length").GetInt32()))).ToArray();
        }
        public string Key(string value) => throw new NotSupportedException();
        public bool IsPatternText(string value) => throw new NotSupportedException();
        public bool FirstIsUppercase(string value) => throw new NotSupportedException();
    }
    private sealed record Call(LearningExplanationInput Input, CancellationToken Token)
    { public TaskCompletionSource<LearningExplanationRecord> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class Service : ILearningExplanationGenerating
    {
        public event Action? Changed;
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public LearningLanguage Language => LearningLanguage.English; public bool IsConfigured => true;
        public int Generations, Displays;
        public Func<LearningExplanationInput, LearningExplanationRecord?>? Saved;
        private readonly Channel<Call> _started = Channel.CreateUnbounded<Call>();
        private readonly ConcurrentQueue<Call> _pending = new();
        public Task<Call> Next() => _started.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Generations); var call = new Call(input, cancellationToken); _pending.Enqueue(call); _started.Writer.TryWrite(call); return call.Completion.Task; }
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => Saved?.Invoke(input);
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult<LearningExplanationRecord?>(null);
        public void RecordDisplay(string requestId) => Interlocked.Increment(ref Displays);
        public void Release() { while (_pending.TryDequeue(out var call)) call.Completion.TrySetResult(Record(call.Input, Language)); }
        public void Change() => Changed?.Invoke();
    }
}
