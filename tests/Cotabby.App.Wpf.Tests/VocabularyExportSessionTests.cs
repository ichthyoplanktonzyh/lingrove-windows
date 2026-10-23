using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual dispatcher session and app-owned Store/archive. A pre-Store gate controls
/// completion order only; archive selection/encoding and successful native save remain real.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportSessionTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-session-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task OriginalMacScopeOptionsIdentityAndCancellationState(string id) => dispatcher.RunAsync(async () =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyExportSession.swift", row.GetProperty("macSource").GetString());
        var actual = JsonSerializer.SerializeToNode(await Observe(row.GetProperty("mode").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    });
    private static async Task<object> Observe(string mode)
    {
        using var folder = new VocabularyExportFileTests.Folder();
        var archive = new VocabularyArchive((string?)null); await archive.CommitAsync(Seed());
        await using var store = new VocabularyStore(archive, SynchronizationContext.Current!); await store.LoadAsync();
        var request = Request(mode); var entered = NewSignal(); var release = NewSignal();
        bool held = mode is "closeDuringPrepare" or "scopeDuringPrepare" or "latestWins";
        async Task<VocabularyExportPackage> Prepare(IReadOnlyList<VocabularySelection> ids, VocabularyExportConfiguration options, CancellationToken token)
        {
            if (held) { entered.TrySetResult(); await release.Task; }
            return await store.PrepareExportAsync(ids, options, token);
        }
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, request, Prepare, store.WriteExportAsync);
        bool? saved = null;
        if (mode == "initial") return State(s, saved, folder.Export, store);
        if (mode == "filtered") s.Scope = VocabularyExportScope.Filtered;
        if (held)
        {
            Task first = s.PrepareAsync(), second = Task.CompletedTask;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (mode == "closeDuringPrepare") s.Cancel(); else s.Scope = VocabularyExportScope.Filtered;
                if (mode == "latestWins") second = s.PrepareAsync();
            }
            finally { release.TrySetResult(); }
            await first.WaitAsync(TimeSpan.FromSeconds(10)); await second.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else await s.PrepareAsync(new(mode is "cancelledPrepare" or "cancelledEmpty"));
        if (mode == "scopeChanged") s.Scope = VocabularyExportScope.Filtered;
        if (mode == "configurationChanged") s.Configuration = s.Configuration with { Format = VocabularyExportFormat.Json };
        if (mode is "wrongPackage" or "save" or "closedSave" or "cancelledSave" && s.Package is { } p)
        {
            if (mode == "wrongPackage") saved = await s.SaveAsync(VocabularyExportPolicy.Package(Seed().Entries, s.Configuration), folder.Export);
            else if (mode == "closedSave") { s.Cancel(); saved = await s.SaveAsync(p, folder.Export); }
            else saved = await s.SaveAsync(p, folder.Export, new(mode == "cancelledSave"));
        }
        if (File.Exists(folder.Export)) Assert.Equal(s.Package!.CopyData(), File.ReadAllBytes(folder.Export));
        return State(s, saved, folder.Export, store);
    }
    private static object State(VocabularyExportSession s, bool? saved, string file, VocabularyStore store) => new {
        scope = s.Scope.ToString().ToLowerInvariant(), format = s.Configuration.Format.Code(), loading = s.Loading, saving = s.Saving,
        active = s.IsActive, canExport = s.CanExport, message = s.Message, saved, written = File.Exists(file), storeError = store.ErrorMessage, storeEntries = store.Entries.Count,
        package = s.Package is { } p ? new { format = p.Configuration.Format.Code(), filename = p.Filename, byteCount = p.DataByteCount,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(p.CopyData())), entryCount = p.EntryCount, senseCount = p.SenseCount } : null
    };
    [Fact]
    public Task LateFailureCannotReplaceANewerSuccessfulPreview() => dispatcher.RunAsync(async () =>
    {
        var first = new TaskCompletionSource<VocabularyExportPackage>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        var package = VocabularyExportPolicy.Package(Seed().Entries, new());
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Request("prepare"),
            (_, _, _) => ++calls == 1 ? first.Task : Task.FromResult(package), (_, _, _) => Task.CompletedTask);
        Task old = s.PrepareAsync(); await s.PrepareAsync(); first.SetException(new IOException("late failure")); await old;
        Assert.Same(package, s.Package); Assert.Null(s.Message); Assert.True(s.CanExport); Assert.False(s.Loading);
    });
    [Fact]
    public Task SaveOwnsExactPackageAndRejectsReentryUntilAcknowledged() => dispatcher.RunAsync(async () =>
    {
        var release = NewSignal(); int writes = 0; VocabularyExportPackage? captured = null; string? path = null;
        var package = VocabularyExportPolicy.Package(Seed().Entries, new());
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Request("prepare"),
            (_, _, _) => Task.FromResult(package), async (p, destination, _) => { writes++; captured = p; path = destination; await release.Task; });
        await s.PrepareAsync(); Task<bool> saving = s.SaveAsync(package, "chosen");
        try { Assert.True(s.Saving); Assert.False(s.CanExport); Assert.False(await s.SaveAsync(package, "other")); }
        finally { release.TrySetResult(); }
        Assert.True(await saving); Assert.Equal(1, writes); Assert.Same(package, captured); Assert.Equal("chosen", path); Assert.False(s.Saving);
    });
    [Fact]
    public Task ClosingAfterWriteAdmissionStillAcknowledgesCommittedSave() => dispatcher.RunAsync(async () =>
    {
        var release = NewSignal(); var package = VocabularyExportPolicy.Package(Seed().Entries, new());
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Request("prepare"),
            (_, _, _) => Task.FromResult(package), async (_, _, _) => await release.Task);
        await s.PrepareAsync(); Task<bool> saving = s.SaveAsync(package, "chosen"); s.Cancel(); release.TrySetResult();
        Assert.True(await saving); Assert.False(s.IsActive); Assert.Null(s.Package); Assert.False(s.CanExport);
    });
    [Fact]
    public Task FailureStaysInThisSessionAndAllowsRetry() => dispatcher.RunAsync(async () =>
    {
        int writes = 0; var package = VocabularyExportPolicy.Package(Seed().Entries, new());
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Request("prepare"),
            (_, _, _) => Task.FromResult(package), (_, _, _) => ++writes == 1 ? Task.FromException(new IOException("fixed failure")) : Task.CompletedTask);
        await s.PrepareAsync(); Assert.False(await s.SaveAsync(package, "chosen")); Assert.Equal("fixed failure", s.Message); Assert.True(s.CanExport);
        Assert.True(await s.SaveAsync(package, "chosen")); Assert.Null(s.Message); Assert.Equal(2, writes);
    });
    [Fact]
    public Task ScopeAndOptionChangesRevokeSaveBeforeAnotherPreparationStarts() => dispatcher.RunAsync(async () =>
    {
        int writes = 0; var package = VocabularyExportPolicy.Package(Seed().Entries, new());
        var s = new VocabularyExportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Request("prepare"),
            (_, _, _) => Task.FromResult(package), (_, _, _) => { writes++; return Task.CompletedTask; });
        await s.PrepareAsync(); s.Scope = VocabularyExportScope.Filtered; Assert.False(await s.SaveAsync(package, "chosen"));
        s.Scope = VocabularyExportScope.All; s.Configuration = s.Configuration with { Notes = false }; Assert.False(await s.SaveAsync(package, "chosen")); Assert.Equal(0, writes);
    });
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static VocabularyExportRequest Request(string mode) => new(
        [new(Id(1), mode == "stale" ? Id(99) : Id(2))], [new(Id(1), Id(4))], [],
        mode is "empty" or "cancelledEmpty" ? VocabularyExportScope.Selected : VocabularyExportScope.All);
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12"));
    private static VocabularyDocument Seed()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new(Id(2), "core", now, now), new(Id(4), "second", now, now)], now, now)] };
    }
}
