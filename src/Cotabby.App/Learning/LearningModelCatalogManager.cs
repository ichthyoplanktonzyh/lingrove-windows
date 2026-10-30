using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Inference;

namespace Cotabby.App.Learning;

/// <summary>
/// Owns explicit learning-model catalog downloads, imports, and installed-file discovery. The
/// manager shares the app's physical GGUF folder so users do not store duplicate weights, while
/// keeping download state and the native learning runtime independent from autocomplete.
/// </summary>
/// <remarks>
/// File enumeration, hashing, downloads, and multi-gigabyte imports run asynchronously away from
/// WPF. This object never selects an autocomplete model, loads native weights, or prewarms a model;
/// the learning controller asks the separate learning runtime to open a file only after a manual
/// translation request.
/// </remarks>
internal sealed class LearningModelCatalogManager : IAsyncDisposable
{
    private static readonly byte[] GgufMagic = "GGUF"u8.ToArray();
    private readonly string? _modelsDirectoryOverride;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifetimeGate = new();
    private readonly HashSet<Task> _operations = [];
    private Task? _disposeTask;
    private int _disposed;
    internal ModelDownloadActivity Activity { get; } = new();

    /// <summary>
    /// Creates the production manager for Cotabby's shared physical model folder. The optional
    /// directory and caller-owned client form a narrow seam for fake-driven transfer tests; the
    /// app keeps the default constructor and owns no additional model service graph.
    /// </summary>
    internal LearningModelCatalogManager(string? modelsDirectory = null, HttpClient? httpClient = null)
    {
        _modelsDirectoryOverride = modelsDirectory is null ? null : Path.GetFullPath(modelsDirectory);
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public string ModelsDirectory => _modelsDirectoryOverride ?? ModelCatalog.DefaultLocalDirectory();

    /// <summary>Scans only the app model directory from a worker thread; settings load never calls this synchronously.</summary>
    public Task<IReadOnlyList<LearningInstalledModel>> GetInstalledModelsAsync(CancellationToken ct = default) =>
        RunOperationAsync<IReadOnlyList<LearningInstalledModel>>(async token =>
        {
            token.ThrowIfCancellationRequested();
            var directory = ModelsDirectory;
            if (!Directory.Exists(directory)) return Array.Empty<LearningInstalledModel>();

            var installed = new List<LearningInstalledModel>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length < GgufMagic.Length
                        || !await HasGgufHeaderAsync(path, token).ConfigureAwait(false)) continue;
                    installed.Add(new LearningInstalledModel(
                        info.Name,
                        Path.GetFileNameWithoutExtension(info.Name),
                        info.Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // One inaccessible file must not hide other models from the picker.
                }
            }

            return installed.OrderBy(model => model.Filename, StringComparer.OrdinalIgnoreCase).ToArray();
        }, ct);

    /// <summary>
    /// Reads a detached assignment snapshot for a task-scoped picker. Discovery alone never grants
    /// an unknown file a task; the shared settings owner records explicit choices separately.
    /// </summary>
    public async Task<IReadOnlyList<LearningInstalledModel>> GetInstalledModelsForTaskAsync(
        LocalModelTask task, IReadOnlyDictionary<string, string[]> assignments, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var snapshot = assignments.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        var models = await GetInstalledModelsAsync(ct).ConfigureAwait(false);
        return models.Where(model => LocalModelTaskPolicy.IsCandidate(model.Filename, task,
            LocalModelTaskAssignments.TasksFor(model.Filename, snapshot))).ToArray();
    }

    /// <summary>
    /// Downloads one curated model only after its Get button was pressed. A verified staging file
    /// is atomically promoted; an existing same-name file is never overwritten without consent.
    /// </summary>
    public Task DownloadAsync(
        LearningDownloadableModel model,
        IProgress<LearningModelTransferProgress>? progress,
        CancellationToken ct, Func<Task>? acknowledgeInstall = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(model);
        ValidateCatalogModel(model);
        // Start even directory checks and collision verification on a worker. A user may point
        // the shared model folder at a slow or removable drive, and WPF must stay responsive.
        return RunOperationAsync(async token =>
        {
            await DownloadCoreAsync(model, progress, token, acknowledgeInstall).ConfigureAwait(false);
            return true;
        }, ct);
    }

    private async Task DownloadCoreAsync(
        LearningDownloadableModel model,
        IProgress<LearningModelTransferProgress>? progress,
        CancellationToken ct, Func<Task>? acknowledgeInstall)
    {
        var gate = _downloadGates.GetOrAdd(model.Filename, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = ModelsDirectory;
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, model.Filename);
            using var fileLease = await ModelFileOperations.AcquireAsync(destination, ct).ConfigureAwait(false);
            if (File.Exists(destination))
            {
                if (await IsVerifiedCatalogFileAsync(destination, model, ct).ConfigureAwait(false))
                {
                    if (acknowledgeInstall is not null) await acknowledgeInstall().ConfigureAwait(false);
                    return;
                }
                throw new IOException("A file with this name already exists and does not match the catalog copy. Rename or remove it before downloading.");
            }

            using var activity = Activity.Begin(model.Filename);
            var staging = Path.Combine(directory, $".learning-{Guid.NewGuid():N}.part");
            try
            {
                using var response = await _httpClient.GetAsync(
                    model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long? responseLength = response.Content.Headers.ContentLength;
                long? total = model.ExpectedSizeBytes ?? responseLength;
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                long copied = 0;
                await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[1 << 16];
                    var lastProgressReport = DateTime.UtcNow;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        copied += read;
                        if (model.ExpectedSizeBytes is { } expected && copied > expected)
                            throw new InvalidDataException("The model download exceeded its catalog size.");
                        digest.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        if (DateTime.UtcNow - lastProgressReport >= TimeSpan.FromMilliseconds(250))
                        {
                            progress?.Report(new LearningModelTransferProgress(copied, total));
                            lastProgressReport = DateTime.UtcNow;
                        }
                    }

                    await output.FlushAsync(ct).ConfigureAwait(false);
                }

                // Close the exclusive staging writer before reopening the file for signature
                // verification and atomic promotion. Windows enforces FileShare.None even for the
                // same process, so leaving an await-using declaration in this outer scope makes
                // every otherwise-valid download fail when the header reader opens this path.
                if (model.ExpectedSizeBytes is { } expectedSize && copied != expectedSize)
                    throw new InvalidDataException("The model download did not match its catalog size.");
                var actualHash = Convert.ToHexString(digest.GetHashAndReset());
                if (model.Sha256 is { Length: > 0 } expectedHash
                    && !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                {
                    throw new InvalidDataException("The model download did not match its catalog checksum.");
                }

                if (!await HasGgufHeaderAsync(staging, ct).ConfigureAwait(false))
                    throw new InvalidDataException("The downloaded file is not a GGUF model.");

                try
                {
                    File.Move(staging, destination, overwrite: false);
                }
                catch (IOException) when (File.Exists(destination))
                {
                    // Another process may have installed this exact catalog file during transfer.
                    if (!await IsVerifiedCatalogFileAsync(destination, model, ct).ConfigureAwait(false)) throw;
                }

                if (acknowledgeInstall is not null) await acknowledgeInstall().ConfigureAwait(false);
                progress?.Report(new LearningModelTransferProgress(copied, total, Completed: true));
            }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Copies selected user GGUFs into the shared installed picker without replacing an existing
    /// file. Import validates the GGUF signature and stages the copy before exposing its final name.
    /// </summary>
    public Task<LearningModelImportResult> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        CancellationToken ct, Func<string, Task>? acknowledgeImport = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var detachedPaths = sourcePaths.ToArray();
        return RunOperationAsync(token => ImportCoreAsync(detachedPaths, token, acknowledgeImport), ct);
    }

    private async Task<LearningModelImportResult> ImportCoreAsync(
        IReadOnlyList<string> sourcePaths,
        CancellationToken ct, Func<string, Task>? acknowledgeImport)
    {
        var directory = ModelsDirectory;
        Directory.CreateDirectory(directory);
        var imported = new List<string>();
        int skipped = 0, failed = 0;
        bool cancelled = false;

        foreach (var sourcePath in sourcePaths)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            var filename = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(filename)
                || !string.Equals(Path.GetExtension(filename), ".gguf", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var destination = Path.Combine(directory, filename);
            var staging = Path.Combine(directory, $".learning-import-{Guid.NewGuid():N}.part");
            try
            {
                using var fileLease = await ModelFileOperations.AcquireAsync(destination, ct).ConfigureAwait(false);
                if (!File.Exists(sourcePath))
                {
                    failed++;
                    continue;
                }

                var sourceFullPath = Path.GetFullPath(sourcePath);
                var destinationFullPath = Path.GetFullPath(destination);
                if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase)
                    || File.Exists(destination))
                {
                    skipped++;
                    continue;
                }

                await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var header = new byte[GgufMagic.Length];
                    if (input.Length < header.Length)
                    {
                        skipped++;
                        continue;
                    }

                    await input.ReadExactlyAsync(header, ct).ConfigureAwait(false);
                    if (!header.AsSpan().SequenceEqual(GgufMagic))
                    {
                        skipped++;
                        continue;
                    }

                    input.Position = 0;
                    await using var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(output, 1 << 16, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                }

                bool published;
                try { File.Move(staging, destination, overwrite: false); published = true; }
                catch (IOException) when (File.Exists(destination))
                {
                    published = false; skipped++;
                }
                if (published)
                {
                    if (acknowledgeImport is not null) await acknowledgeImport(filename).ConfigureAwait(false);
                    imported.Add(filename);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                failed++;
            }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return new LearningModelImportResult(imported.AsReadOnly(), skipped, failed, cancelled);
    }

    /// <summary>Waits for transfers of this exact app-owned leaf, then delegates the final
    /// recheck and unlink to the UI owner as one serialized operation.</summary>
    public Task<bool> DeleteAsync(string filename, Func<string, Task<bool>> unlink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unlink);
        if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || !string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(filename), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A model filename is required.", nameof(filename));
        var path = Path.Combine(ModelsDirectory, filename);
        return RunOperationAsync(async token =>
        {
            using var lease = await ModelFileOperations.AcquireAsync(path, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return await unlink(path).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>
    /// Closes admission before cancelling transfers, then waits for their staging-file cleanup.
    /// AppHost awaits this boundary during shutdown; the shared model directory is never removed.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task[] active;
        TaskCompletionSource completion;
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            Volatile.Write(ref _disposed, 1);
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            active = _operations.ToArray();
        }

        // Cancellation callbacks can run arbitrary transport code, so never invoke them while
        // holding the admission lock. Operations keep their linked token source until they finish.
        _ = FinishDisposalAsync(active, completion);
        return new ValueTask(completion.Task);
    }

    private Task<T> RunOperationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            var task = Task.Run(async () =>
            {
                linked.Token.ThrowIfCancellationRequested();
                return await action(linked.Token).ConfigureAwait(false);
            }, CancellationToken.None);
            _operations.Add(task);
            _ = ObserveOperationAsync(task, linked);
            return task;
        }
    }

    private async Task ObserveOperationAsync(Task task, CancellationTokenSource linked)
    {
        try { await task.ConfigureAwait(false); }
        catch { /* The returned task carries the failure to the requesting settings view. */ }
        finally
        {
            linked.Dispose();
            lock (_lifetimeGate) _operations.Remove(task);
        }
    }

    private async Task FinishDisposalAsync(Task[] active, TaskCompletionSource completion)
    {
        try
        {
            try { _shutdown.Cancel(); }
            catch (AggregateException) { /* Still drain after a transport cancellation callback fails. */ }
            try { await Task.WhenAll(active).ConfigureAwait(false); }
            catch { /* Cancelled/failed operations have already run their staging-file finally blocks. */ }
            if (_ownsHttpClient) _httpClient.Dispose();
            _shutdown.Dispose();
            foreach (var gate in _downloadGates.Values) gate.Dispose();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private static void ValidateCatalogModel(LearningDownloadableModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Filename)
            || model.Filename.IndexOfAny(['/', '\\']) >= 0
            || !string.Equals(Path.GetFileName(model.Filename), model.Filename, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(model.Filename), ".gguf", StringComparison.OrdinalIgnoreCase)
            || model.DownloadUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The learning model catalog entry is invalid.");
        }
    }

    private static async Task<bool> IsVerifiedCatalogFileAsync(
        string path, LearningDownloadableModel model, CancellationToken ct)
    {
        if (model.ExpectedSizeBytes is { } expected && new FileInfo(path).Length != expected) return false;
        if (!await HasGgufHeaderAsync(path, ct).ConfigureAwait(false)) return false;
        if (string.IsNullOrWhiteSpace(model.Sha256)) return true;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(model.Sha256));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<bool> HasGgufHeaderAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                GgufMagic.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = new byte[GgufMagic.Length];
            await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            return header.AsSpan().SequenceEqual(GgufMagic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed record LearningInstalledModel(string Filename, string DisplayName, long SizeBytes);
internal sealed record LearningModelTransferProgress(long BytesDownloaded, long? TotalBytes, bool Completed = false);
internal sealed record LearningModelImportResult(IReadOnlyList<string> ImportedFilenames, int Skipped, int Failed, bool Cancelled)
{
    public int Imported => ImportedFilenames.Count;
}
