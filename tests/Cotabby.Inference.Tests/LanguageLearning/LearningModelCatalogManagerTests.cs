using System.Net;
using System.Security.Cryptography;
using System.Text;
using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Inference;

namespace Cotabby.Inference.Tests.LanguageLearning;

/// <summary>
/// Exercises the production file-transfer boundary with tiny synthetic signatures and an in-memory
/// HTTP handler. No test downloads weights, opens a native model, or touches the user's model folder.
/// </summary>
public sealed class LearningModelCatalogManagerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("GGUFsynthetic-transfer-fixture");

    [Fact]
    public async Task TaskScopedDiscoveryRequiresAssignmentAndRejectsOtherCatalogTasks()
    {
        using var files = new TransferFiles();
        Directory.CreateDirectory(files.Models);
        foreach (var name in new[] { "custom.gguf", "Hy-MT2-1.8B-Q4_K_M.gguf", "Gemma-4-E4B-Q4_K_M.gguf", "mmproj-custom.gguf" })
            await File.WriteAllBytesAsync(Path.Combine(files.Models, name), Payload);
        await using var manager = new LearningModelCatalogManager(files.Models);
        var assignments = new Dictionary<string, string[]>();
        var before = await manager.GetInstalledModelsForTaskAsync(LocalModelTask.Translation, assignments);
        Assert.Equal(new[] { "Hy-MT2-1.8B-Q4_K_M.gguf" }, before.Select(model => model.Filename));
        Assert.True(LocalModelTaskAssignments.TryRecord("custom.gguf", LocalModelTask.Translation, assignments));
        var after = await manager.GetInstalledModelsForTaskAsync(LocalModelTask.Translation, assignments);
        Assert.Equal(new[] { "custom.gguf", "Hy-MT2-1.8B-Q4_K_M.gguf" }, after.Select(model => model.Filename));
        var explanation = await manager.GetInstalledModelsForTaskAsync(LocalModelTask.Explanation, assignments);
        Assert.Equal(new[] { "Gemma-4-E4B-Q4_K_M.gguf" }, explanation.Select(model => model.Filename));
    }

    [Fact]
    public async Task VerifiedDownloadPromotesAClosedFileAndReusesAnExactExistingCopy()
    {
        using var files = new TransferFiles();
        using var handler = new FakeHandler(() => new MemoryStream(Payload, writable: false));
        using var http = new HttpClient(handler);
        await using var manager = new LearningModelCatalogManager(files.Models, http);
        var model = Entry(Payload);

        await manager.DownloadAsync(model, null, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Path.Combine(files.Models, model.Filename)));
        Assert.Empty(Directory.GetFiles(files.Models, "*.part"));

        await manager.DownloadAsync(model, null, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("signature")]
    public async Task InvalidTransferNeverPromotesAndRemovesItsStagingFile(string failure)
    {
        using var files = new TransferFiles();
        var bytes = failure == "signature" ? Encoding.ASCII.GetBytes("this-is-not-a-gguf") : Payload;
        using var handler = new FakeHandler(() => new MemoryStream(bytes, writable: false));
        using var http = new HttpClient(handler);
        await using var manager = new LearningModelCatalogManager(files.Models, http);
        var model = Entry(bytes) with
        {
            Sha256 = failure == "hash" ? new string('0', 64) : Hash(bytes),
            ExpectedSizeBytes = bytes.Length + (failure == "size" ? 1 : 0),
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.DownloadAsync(model, null, CancellationToken.None).WaitAsync(Timeout));

        Assert.Empty(Directory.GetFiles(files.Models));
    }

    [Fact]
    public async Task ExistingDifferentModelIsPreservedWithoutAnyNetworkRequest()
    {
        using var files = new TransferFiles();
        Directory.CreateDirectory(files.Models);
        var existing = Encoding.ASCII.GetBytes("GGUFthe-user-already-owns-this-file");
        var destination = Path.Combine(files.Models, "fixture.gguf");
        await File.WriteAllBytesAsync(destination, existing);
        using var handler = new FakeHandler(() => new MemoryStream(Payload, writable: false));
        using var http = new HttpClient(handler);
        await using var manager = new LearningModelCatalogManager(files.Models, http);

        await Assert.ThrowsAsync<IOException>(() =>
            manager.DownloadAsync(Entry(Payload), null, CancellationToken.None).WaitAsync(Timeout));

        Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("../escape.gguf")]
    [InlineData("..\\escape.gguf")]
    [InlineData("not-a-model.txt")]
    public async Task InvalidCatalogDestinationIsRejectedBeforeFileOrNetworkWork(string filename)
    {
        using var files = new TransferFiles();
        using var handler = new FakeHandler(() => new MemoryStream(Payload, writable: false));
        using var http = new HttpClient(handler);
        await using var manager = new LearningModelCatalogManager(files.Models, http);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await manager.DownloadAsync(Entry(Payload) with { Filename = filename }, null, CancellationToken.None).WaitAsync(Timeout));

        Assert.False(Directory.Exists(files.Models));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ImportPreservesCollisionsAndDiscoverySkipsMislabeledFiles()
    {
        using var files = new TransferFiles();
        Directory.CreateDirectory(files.Models);
        Directory.CreateDirectory(files.Sources);
        var source = Path.Combine(files.Sources, "valid.gguf");
        var malformed = Path.Combine(files.Sources, "bad.gguf");
        await File.WriteAllBytesAsync(source, Payload);
        await File.WriteAllTextAsync(malformed, "not a GGUF");
        await using var manager = new LearningModelCatalogManager(files.Models);

        var imported = await manager.ImportAsync([source, malformed], CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(1, imported.Imported);
        Assert.Equal(1, imported.Skipped);
        var collision = await manager.ImportAsync([source], CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(1, collision.Skipped);
        await File.WriteAllTextAsync(Path.Combine(files.Models, "manually-dropped.gguf"), "not a GGUF");

        var installed = await manager.GetInstalledModelsAsync().WaitAsync(Timeout);

        Assert.Equal(["valid.gguf"], installed.Select(model => model.Filename));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Path.Combine(files.Models, "valid.gguf")));
        Assert.Empty(Directory.GetFiles(files.Models, "*.part"));
    }

    [Fact]
    public async Task ImportAcknowledgesEachPublishedFileBeforeCancellationAndReleasesItsGate()
    {
        using var files = new TransferFiles(); Directory.CreateDirectory(files.Sources);
        var first = Path.Combine(files.Sources, "first.gguf"); var second = Path.Combine(files.Sources, "second.gguf");
        await File.WriteAllBytesAsync(first, Payload); await File.WriteAllBytesAsync(second, Payload);
        await using var manager = new LearningModelCatalogManager(files.Models);
        using var stop = new CancellationTokenSource(); var acknowledged = new List<string>();
        var result = await manager.ImportAsync([first, second], stop.Token, filename =>
        { acknowledged.Add(filename); stop.Cancel(); return Task.CompletedTask; }).WaitAsync(Timeout);
        Assert.Equal(["first.gguf"], acknowledged); Assert.Equal(acknowledged, result.ImportedFilenames);
        Assert.True(result.Cancelled); Assert.True(File.Exists(Path.Combine(files.Models, "first.gguf")));
        Assert.False(File.Exists(Path.Combine(files.Models, "second.gguf")));
        using var lease = await ModelFileOperations.AcquireAsync(Path.Combine(files.Models, "FIRST.gguf"), CancellationToken.None).WaitAsync(Timeout);
    }

    [Fact]
    public async Task DeleteWaitsForSameFileAndRechecksAfterTheWait()
    {
        using var files = new TransferFiles(); Directory.CreateDirectory(files.Models);
        string path = Path.Combine(files.Models, "model.gguf"); await File.WriteAllBytesAsync(path, Payload);
        await using var manager = new LearningModelCatalogManager(files.Models);
        using var lease = await ModelFileOperations.AcquireAsync(path, CancellationToken.None);
        bool protectedNow = false;
        var deletion = manager.DeleteAsync("MODEL.gguf", target =>
        {
            if (protectedNow) return Task.FromResult(false);
            File.Delete(target); return Task.FromResult(true);
        }, CancellationToken.None);
        protectedNow = true; Assert.False(deletion.IsCompleted); lease.Dispose();
        Assert.False(await deletion.WaitAsync(Timeout)); Assert.True(File.Exists(path));
        await Assert.ThrowsAsync<ArgumentException>(async () => await manager.DeleteAsync("../model.gguf", _ => Task.FromResult(true), CancellationToken.None));
    }

    [Fact]
    public async Task PublishedImportKeepsDestinationLeaseUntilMetadataAcknowledgement()
    {
        using var files = new TransferFiles(); Directory.CreateDirectory(files.Sources);
        var source = Path.Combine(files.Sources, "model.gguf"); await File.WriteAllBytesAsync(source, Payload);
        await using var manager = new LearningModelCatalogManager(files.Models);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var import = manager.ImportAsync([source], CancellationToken.None, async _ => { started.SetResult(); await release.Task; });
        await started.Task.WaitAsync(Timeout);
        var waiting = ModelFileOperations.AcquireAsync(Path.Combine(files.Models, "MODEL.gguf"), CancellationToken.None);
        Assert.False(waiting.IsCompleted);
        release.SetResult(); Assert.Equal(1, (await import.WaitAsync(Timeout)).Imported);
        using var lease = await waiting.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ShutdownClosesAdmissionAndDrainsTransferCleanupBeforeReturning()
    {
        using var files = new TransferFiles();
        using var body = new GatedBody();
        int opened = 0;
        using var handler = new FakeHandler(() => Interlocked.Increment(ref opened) == 1
            ? body : new MemoryStream(Payload, writable: false));
        using var http = new HttpClient(handler);
        var manager = new LearningModelCatalogManager(files.Models, http);
        var transfer = manager.DownloadAsync(Entry(Payload), null, CancellationToken.None);
        Task? stopping = null;
        try
        {
            await body.Blocked.Task.WaitAsync(Timeout);
            Assert.True(manager.Activity.IsDownloading(Entry(Payload).Filename));
            stopping = manager.DisposeAsync().AsTask();
            Assert.False(stopping.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.GetInstalledModelsAsync());

            body.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(Timeout));
            await stopping.WaitAsync(Timeout);
            Assert.False(manager.Activity.IsDownloading(Entry(Payload).Filename));

            Assert.Empty(Directory.GetFiles(files.Models));
            // An injected HTTP client remains owned by the caller after manager shutdown.
            using var response = await http.GetAsync("https://models.example.test/still-caller-owned").WaitAsync(Timeout);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            body.Release.TrySetResult();
            try { await transfer.WaitAsync(Timeout); } catch { /* Observe cleanup without hiding the original assertion. */ }
            await (stopping ?? manager.DisposeAsync().AsTask()).WaitAsync(Timeout);
        }
    }

    private static LearningDownloadableModel Entry(byte[] bytes) => new(
        "fixture.gguf", "Synthetic fixture", new Uri("https://models.example.test/fixture.gguf"),
        0, bytes.Length, Hash(bytes));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class TransferFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-transfer-" + Guid.NewGuid().ToString("N"));
        public string Models => Path.Combine(_root, "models");
        public string Sources => Path.Combine(_root, "sources");
        public TransferFiles() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeHandler(Func<Stream> openBody) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(openBody()),
                RequestMessage = request,
            });
        }
    }

    private sealed class GatedBody : MemoryStream
    {
        private int _reads;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GatedBody() : base(Payload, writable: false) { }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _reads) == 1)
                return await base.ReadAsync(buffer[..Math.Min(4, buffer.Length)], ct);
            Blocked.TrySetResult();
            await Release.Task;
            ct.ThrowIfCancellationRequested();
            return 0;
        }
    }
}
