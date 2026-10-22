using System.Text;
using Cotabby.Core.LanguageLearning;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// Owns one learning-only weights/context allocation. An operation lease serializes model changes,
/// prompt tokenization, and generation; cancellation is cooperative between native calls because
/// LLamaSharp 0.27 cannot abort a Decode already executing in llama.cpp.
/// </summary>
/// <remarks>
/// The runtime is long-lived and is disposed by the app host. Each operation holds the runtime's
/// gate from model preparation through facade planning and generation. Disposal closes admission,
/// cancels the active operation, waits for any in-flight native call to return, then disposes the
/// context before its parent weights. No timeout permits freeing state still used by native code.
/// </remarks>
public sealed class LearningLocalRuntime : ILearningLocalRuntime
{
    private readonly ILearningLocalNativeSessionFactory _sessionFactory;
    private readonly ILogger<LearningLocalRuntime> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleLock = new();

    private ILearningLocalNativeSession? _session;
    private RuntimeIdentity? _identity;
    private LearningLocalOperation? _activeOperation;
    private Task? _disposeTask;
    private int _disposeRequested;
    private long _retirementEpoch;
    private long _residencyVersion;

    /// <summary>Creates the production LLamaSharp owner; model files are never downloaded.</summary>
    public LearningLocalRuntime(ILogger<LearningLocalRuntime>? logger = null, ILoggerFactory? loggerFactory = null)
        : this(
            new LLamaLearningLocalNativeSessionFactory(
                loggerFactory ?? NullLoggerFactory.Instance,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LLamaLearningLocalNativeSessionFactory>()),
            logger ?? NullLogger<LearningLocalRuntime>.Instance)
    {
    }

    internal LearningLocalRuntime(
        ILearningLocalNativeSessionFactory sessionFactory,
        ILogger<LearningLocalRuntime>? logger = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _logger = logger ?? NullLogger<LearningLocalRuntime>.Instance;
    }

    /// <inheritdoc />
    public async Task<ILearningLocalOperation> BeginOperationAsync(
        LearningLocalModelConfiguration configuration,
        CancellationToken ct,
        CancellationToken ownerCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        long admittedEpoch;
        lock (_lifecycleLock)
        {
            ThrowIfDisposing();
            ct.ThrowIfCancellationRequested();
            ownerCancellation.ThrowIfCancellationRequested();
            admittedEpoch = _retirementEpoch;
        }

        using var admission = CancellationTokenSource.CreateLinkedTokenSource(ct, ownerCancellation, _shutdown.Token);
        try
        {
            await _operationGate.WaitAsync(admission.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed);
        }

        bool transferGate = false;
        try
        {
            ThrowIfDisposing();
            lock (_lifecycleLock) ThrowIfRetired(admittedEpoch);
            await EnsureSessionAsync(configuration, admission.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ownerCancellation.ThrowIfCancellationRequested();
            ThrowIfDisposing();

            LearningLocalOperation operation;
            lock (_lifecycleLock)
            {
                ThrowIfDisposing();
                ct.ThrowIfCancellationRequested();
                ownerCancellation.ThrowIfCancellationRequested();
                // Unload may have arrived while synchronous native loading was still in flight.
                // Never hand that retired allocation to a caller when the load finally returns.
                ThrowIfRetired(admittedEpoch);

                operation = new LearningLocalOperation(
                    _session!,
                    _shutdown.Token,
                    () => Volatile.Read(ref _retirementEpoch) != admittedEpoch
                        || ct.IsCancellationRequested || ownerCancellation.IsCancellationRequested,
                    ReleaseOperation);
                _activeOperation = operation;
                // Only a successfully created lease supersedes a pending release. A queued Begin
                // that is cancelled or fails must not leave old weights resident without an owner.
                _residencyVersion++;
            }
            transferGate = true;
            return operation;
        }
        catch (OperationCanceledException ex) when (_shutdown.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed, ex);
        }
        finally
        {
            if (!transferGate) _operationGate.Release();
        }
    }

    private async Task EnsureSessionAsync(LearningLocalModelConfiguration configuration, CancellationToken ct)
    {
        RuntimeIdentity identity;
        try
        {
            identity = new RuntimeIdentity(
                Path.GetFullPath(configuration.ModelPath),
                configuration.ContextWindowTokens,
                configuration.GpuLayerCount,
                configuration.RequestedTemplate == LearningChatTemplate.Automatic);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.ModelUnavailable, ex);
        }

        if (_session is not null && _identity == identity) return;

        // Only the operation which owns this gate can change the loaded allocation. Dispose the
        // old child context before its parent weights, then create a fresh, separate learning pair.
        _session?.Dispose();
        _session = null;
        _identity = null;

        try
        {
            var loaded = await _sessionFactory.LoadAsync(configuration, ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                loaded.Dispose();
                throw;
            }
            _session = loaded;
            _identity = identity;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LearningLocalRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.ModelLoadFailed, ex);
        }
    }

    private void ReleaseOperation(LearningLocalOperation operation)
    {
        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_activeOperation, operation)) _activeOperation = null;
        }
        _operationGate.Release();
    }

    /// <inheritdoc />
    public Task UnloadAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeRequested != 0) return _disposeTask ?? Task.CompletedTask;
            Interlocked.Increment(ref _retirementEpoch);
            var version = ++_residencyVersion;
            var active = _activeOperation;
            // Even an idle allocation can take time to free. Keep both cancellation callbacks and
            // native disposal off the caller's WPF thread and outside the lifecycle lock.
            return Task.Run(() => UnloadCoreAsync(version, active));
        }
    }

    private async Task UnloadCoreAsync(long version, LearningLocalOperation? active)
    {
        if (active is not null) await active.DisposeAsync().ConfigureAwait(false);
        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            ILearningLocalNativeSession? retired;
            lock (_lifecycleLock)
            {
                // A newer request or shutdown owns the allocation now. A delayed release must
                // never tear down memory that the new request has just prepared or will reuse.
                if (_disposeRequested != 0 || version != _residencyVersion) return;
                retired = _session;
                _session = null;
                _identity = null;
            }
            retired?.Dispose();
            _logger.LogDebug("Learning local allocation released.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ThrowIfRetired(long admittedEpoch)
    {
        if (admittedEpoch != _retirementEpoch) throw new OperationCanceledException();
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeRequested = 1;
            _disposeTask ??= Task.Run(DisposeCoreAsync);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();

        // Disposing the active lease cooperatively cancels its loop and drains a native Decode
        // before releasing the operation gate. If model loading is still in progress, the linked
        // shutdown token makes that begin operation release the same gate when it unwinds.
        var activeOperation = _activeOperation;
        if (activeOperation is not null)
        {
            await activeOperation.DisposeAsync().ConfigureAwait(false);
        }

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _session?.Dispose();
            _session = null;
            _identity = null;
        }
        finally
        {
            _operationGate.Release();
            _logger.LogDebug("Learning local runtime drained and released.");
        }
    }

    // Manual templates deliberately skip header parsing. Automatic must not inherit a resident
    // session whose metadata was never read; manual-to-manual changes can reuse the allocation.
    private readonly record struct RuntimeIdentity(
        string ModelPath, int ContextTokens, int GpuLayerCount, bool MetadataRequired);

    /// <summary>
    /// A request-local token plan and sampler run while this object owns the parent gate. Calls on
    /// one lease serialize; DisposeAsync rejects new work and does not release the parent gate until
    /// any current LLamaSharp call has returned and its memory has been cleared.
    /// </summary>
    private sealed class LearningLocalOperation : ILearningLocalOperation
    {
        private const int MaximumOutputBytes = 48_000;

        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private readonly ILearningLocalNativeSession _session;
        private readonly LearningGgufMetadata _metadata;
        private readonly int _contextWindowTokens;
        private readonly SemaphoreSlim _callGate = new(1, 1);
        private readonly CancellationTokenSource _stop;
        private readonly CancellationToken _runtimeStop;
        private readonly Func<bool> _isRetired;
        private readonly Action<LearningLocalOperation> _releaseParent;
        private readonly object _lifecycleLock = new();
        private Task? _disposeTask;
        private bool _disposeRequested;

        public LearningLocalOperation(
            ILearningLocalNativeSession session,
            CancellationToken runtimeStop,
            Func<bool> isRetired,
            Action<LearningLocalOperation> releaseParent)
        {
            _session = session;
            // Snapshot native-backed facts while the runtime still holds the parent operation
            // gate. Public getters can then remain safe after this lease is disposed and the
            // runtime reloads or releases its context.
            _metadata = session.Metadata;
            _contextWindowTokens = session.ContextWindowTokens;
            _runtimeStop = runtimeStop;
            _isRetired = isRetired;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(runtimeStop);
            _releaseParent = releaseParent;
        }

        public LearningGgufMetadata Metadata => _metadata;

        public int ContextWindowTokens => _contextWindowTokens;

        public IReadOnlyList<LLamaToken> TokenizeTrusted(string text) => Tokenize(text, allowSpecialTokens: true);

        public IReadOnlyList<LLamaToken> TokenizeLiteral(string text) => Tokenize(text, allowSpecialTokens: false);

        private IReadOnlyList<LLamaToken> Tokenize(string text, bool allowSpecialTokens)
        {
            ArgumentNullException.ThrowIfNull(text);
            EnterCallSynchronously();
            try
            {
                ThrowIfStopped(_stop.Token);
                var tokens = _session.Tokenize(text, allowSpecialTokens).ToArray();
                ThrowIfStopped(_stop.Token);
                return Array.AsReadOnly(tokens);
            }
            finally
            {
                _callGate.Release();
            }
        }

        public async Task<string> GenerateAsync(
            IReadOnlyList<LLamaToken> promptTokens,
            LearningModelSamplingValues sampling,
            int maxTokens,
            Action<string>? onDelta,
            CancellationToken ct)
        {
            if (promptTokens is null || sampling is null || promptTokens.Count == 0 || maxTokens <= 0)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidRequest);
            if (!IsValidSampling(sampling))
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidRequest);

            // Check the caller's immutable plan before copying it or touching native state. The
            // facade validates Core's template-specific policy; this independent boundary guard
            // prevents any caller from decoding a prompt whose output reserve cannot fit the
            // context that was actually loaded (which can be smaller than the requested setting).
            int contextTokens = ContextWindowTokens;
            int promptTokenCount = promptTokens.Count;
            if (promptTokenCount == 0)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidRequest);
            if (IsPromptBudgetExceeded(contextTokens, promptTokenCount, maxTokens))
            {
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.PromptBudgetExceeded);
            }

            // Snapshot the facade's token plan before crossing onto the worker thread. It cannot
            // mutate midway through prefill, even if its owner drops or replaces the plan. Recheck
            // the snapshot length because an arbitrary IReadOnlyList can still mutate while copied.
            var frozenTokens = promptTokens.ToArray();
            if (frozenTokens.Length == 0)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidRequest);
            if (IsPromptBudgetExceeded(contextTokens, frozenTokens.Length, maxTokens))
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.PromptBudgetExceeded);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
            await EnterCallAsync(linked.Token).ConfigureAwait(false);
            try
            {
                // Native Decode is synchronous. Task.Run keeps it off a caller/UI context, while
                // None deliberately avoids pretending Task cancellation can abort an in-flight
                // llama.cpp call. The loop observes the linked token as soon as each call returns.
                return await Task.Run(
                    () => Generate(frozenTokens, sampling, maxTokens, onDelta, linked.Token),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _callGate.Release();
            }
        }

        private void ThrowIfStopped(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Retirement is visible synchronously, even before the unload worker can run its
            // cancellation callbacks. A returning Decode must not sample or publish in that gap.
            if (_isRetired()) throw new OperationCanceledException();
        }

        private static bool IsValidSampling(LearningModelSamplingValues sampling) =>
            double.IsFinite(sampling.Temperature) && sampling.Temperature is >= 0 and <= 2 &&
            double.IsFinite(sampling.TopP) && sampling.TopP is >= 0.01 and <= 1 &&
            sampling.TopK is >= 1 and <= 256 &&
            double.IsFinite(sampling.RepetitionPenalty) && sampling.RepetitionPenalty is >= 1 and <= 2;

        private static bool IsPromptBudgetExceeded(int contextTokens, int promptTokens, int maxTokens) =>
            contextTokens <= 0 || promptTokens >= contextTokens ||
            maxTokens >= contextTokens - promptTokens;

        private string Generate(
            LLamaToken[] promptTokens,
            LearningModelSamplingValues sampling,
            int maxTokens,
            Action<string>? onDelta,
            CancellationToken ct)
        {
            Exception? generationFailure = null;
            try
            {
                _session.ClearMemory();
                ThrowIfStopped(ct);
                int logitsIndex = Prefill(promptTokens, ct);
                using var sampler = _session.CreateSampler(sampling);
                var output = new StrictUtf8Output(MaximumOutputBytes);
                int nextPosition = promptTokens.Length;

                for (int generated = 0; generated < maxTokens; generated++)
                {
                    ThrowIfStopped(ct);
                    LLamaToken token = sampler.Sample(logitsIndex);
                    ThrowIfStopped(ct);
                    if (_session.IsEndOfGeneration(token))
                    {
                        string complete = output.Complete();
                        ThrowIfStopped(ct);
                        return complete;
                    }

                    sampler.Accept(token);
                    ThrowIfStopped(ct);
                    output.Append(_session.TokenToBytes(token));
                    string delta = output.TakeNewText();
                    ThrowIfStopped(ct);
                    if (delta.Length > 0) onDelta?.Invoke(delta);

                    // The final sampled token is not decoded back into KV because no next sample
                    // is permitted. This also leaves maxTokens an exact count of output tokens.
                    if (generated + 1 < maxTokens)
                    {
                        ThrowIfStopped(ct);
                        logitsIndex = _session.DecodeToken(token, nextPosition++);
                        ThrowIfStopped(ct);
                    }
                }

                ThrowIfStopped(ct);
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.OutputLimitReached);
            }
            catch (Exception ex)
            {
                generationFailure = ex;
                throw;
            }
            finally
            {
                // Prefix batches or generated tokens can be partially present after cancellation or
                // a native error. The learning owner never reuses that KV state for a later request.
                try
                {
                    _session.ClearMemory();
                }
                catch (Exception) when (generationFailure is not null)
                {
                    // Preserve the original cancellation/native/callback exception. The allocation
                    // remains owned until the operation lease drains and its context is disposed.
                }
                catch (Exception ex)
                {
                    throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed, ex);
                }
            }
        }

        private int Prefill(IReadOnlyList<LLamaToken> promptTokens, CancellationToken ct)
        {
            int logitsIndex = -1;
            int batchSize = Math.Max(1, _session.BatchSize);
            for (int offset = 0; offset < promptTokens.Count; offset += batchSize)
            {
                ThrowIfStopped(ct);
                int count = Math.Min(batchSize, promptTokens.Count - offset);
                // Only the final prompt token needs logits. DecodeBatch returns that token's row
                // index, which is the starting point for the first sample.
                logitsIndex = _session.DecodeBatch(
                    promptTokens,
                    offset,
                    count,
                    position: offset,
                    requestLogits: offset + count == promptTokens.Count);
                ThrowIfStopped(ct);
            }

            if (logitsIndex < 0)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed);
            return logitsIndex;
        }

        private void EnterCallSynchronously()
        {
            ThrowIfDisposing();
            try
            {
                _callGate.Wait(_stop.Token);
            }
            catch (OperationCanceledException) when (_runtimeStop.IsCancellationRequested)
            {
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed);
            }

            if (_stop.IsCancellationRequested)
            {
                _callGate.Release();
                ThrowIfStopped(_stop.Token);
            }

            lock (_lifecycleLock)
            {
                if (!_disposeRequested) return;
            }

            _callGate.Release();
            ThrowIfDisposing();
        }

        private async Task EnterCallAsync(CancellationToken ct)
        {
            ThrowIfDisposing();
            try
            {
                await _callGate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_runtimeStop.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed);
            }

            lock (_lifecycleLock)
            {
                if (!_disposeRequested) return;
            }

            _callGate.Release();
            ThrowIfDisposing();
        }

        private void ThrowIfDisposing()
        {
            lock (_lifecycleLock)
            {
                if (_disposeRequested)
                    throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.RuntimeDisposed);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_lifecycleLock)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            lock (_lifecycleLock) _disposeRequested = true;
            _stop.Cancel();

            // Wait for a live Task.Run decode to return before releasing the owner's exclusive
            // lease. Queued calls see _disposeRequested and exit without touching native state.
            await _callGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _releaseParent(this);
            }
            finally
            {
                _callGate.Release();
                _stop.Dispose();
            }
        }

        private sealed class StrictUtf8Output
        {
            private readonly Decoder _decoder = StrictUtf8.GetDecoder();
            private readonly StringBuilder _text = new();
            private readonly int _maximumBytes;
            private int _byteCount;
            private int _emittedCharacters;

            public StrictUtf8Output(int maximumBytes) => _maximumBytes = maximumBytes;

            public void Append(byte[] bytes)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                if (bytes.Length > _maximumBytes - _byteCount)
                    throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.OutputTooLarge);
                _byteCount += bytes.Length;
                _text.Append(Decode(bytes, flush: false));
            }

            public string TakeNewText()
            {
                if (_emittedCharacters == _text.Length) return string.Empty;
                string delta = _text.ToString(_emittedCharacters, _text.Length - _emittedCharacters);
                _emittedCharacters = _text.Length;
                return delta;
            }

            public string Complete()
            {
                _text.Append(Decode([], flush: true));
                return _text.ToString();
            }

            private string Decode(byte[] bytes, bool flush)
            {
                var chars = new char[StrictUtf8.GetMaxCharCount(bytes.Length) + 1];
                try
                {
                    _decoder.Convert(
                        bytes,
                        0,
                        bytes.Length,
                        chars,
                        0,
                        chars.Length,
                        flush,
                        out int bytesUsed,
                        out int charsUsed,
                        out bool completed);
                    if (bytesUsed != bytes.Length || !completed)
                        throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidUtf8);
                    return new string(chars, 0, charsUsed);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.InvalidUtf8, ex);
                }
            }
        }
    }

    private sealed class LLamaLearningLocalNativeSessionFactory(
        ILoggerFactory loggerFactory,
        ILogger logger) : ILearningLocalNativeSessionFactory
    {
        public async Task<ILearningLocalNativeSession> LoadAsync(
            LearningLocalModelConfiguration configuration,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(configuration.ModelPath)
                || !File.Exists(configuration.ModelPath))
            {
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.ModelUnavailable);
            }

            var parameters = new ModelParams(configuration.ModelPath)
            {
                ContextSize = (uint)configuration.ContextWindowTokens,
                GpuLayerCount = configuration.GpuLayerCount,
                Threads = Math.Max(1, Environment.ProcessorCount - 1),
                BatchThreads = Math.Max(1, Environment.ProcessorCount - 1),
                BatchSize = 256,
                UBatchSize = 256,
            };

            logger.LogDebug(
                "Loading learning model allocation (ctx={ContextTokens}, gpuLayers={GpuLayers}).",
                configuration.ContextWindowTokens,
                configuration.GpuLayerCount);

            LLamaWeights? weights = null;
            LLamaContext? context = null;
            try
            {
                // Read the bounded header rather than LLamaSharp's metadata convenience APIs,
                // which allocate a value-sized buffer before the caller can apply a byte limit.
                var metadata = configuration.RequestedTemplate == LearningChatTemplate.Automatic
                    ? LearningGgufHeaderReader.ReadFile(configuration.ModelPath, ct)
                    : new LearningGgufMetadata(string.Empty);
                weights = await LLamaWeights.LoadFromFileAsync(parameters, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                context = weights.CreateContext(parameters, loggerFactory.CreateLogger<LLamaContext>());
                return new LLamaLearningLocalNativeSession(
                    weights,
                    context,
                    configuration.ContextWindowTokens,
                    metadata,
                    logger);
            }
            catch (OperationCanceledException)
            {
                context?.Dispose();
                weights?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                context?.Dispose();
                weights?.Dispose();
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.ModelLoadFailed, ex);
            }
        }
    }

    private sealed class LLamaLearningLocalNativeSession : ILearningLocalNativeSession
    {
        private readonly LLamaWeights _weights;
        private readonly LLamaContext _context;
        private readonly ILogger _logger;
        private readonly int _requestedContextTokens;
        private bool _disposed;

        public LLamaLearningLocalNativeSession(
            LLamaWeights weights,
            LLamaContext context,
            int requestedContextTokens,
            LearningGgufMetadata metadata,
            ILogger logger)
        {
            _weights = weights;
            _context = context;
            _requestedContextTokens = requestedContextTokens;
            _logger = logger;
            Metadata = metadata;
        }

        public LearningGgufMetadata Metadata { get; }

        public int ContextWindowTokens => (int)Math.Min(_context.ContextSize, (uint)_requestedContextTokens);

        public int BatchSize => (int)_context.BatchSize;

        public IReadOnlyList<LLamaToken> Tokenize(string text, bool allowSpecialTokens)
        {
            ThrowIfDisposed();
            // All trusted separators are explicitly authored by Core. User text uses special:false,
            // so a literal source spelling such as <|endoftext|> cannot create a model control token.
            return _context.NativeHandle.Tokenize(
                text,
                add_bos: false,
                special: allowSpecialTokens,
                Encoding.UTF8);
        }

        public void ClearMemory()
        {
            ThrowIfDisposed();
            _context.NativeHandle.MemoryClear(true);
        }

        public int DecodeBatch(
            IReadOnlyList<LLamaToken> tokens,
            int offset,
            int count,
            int position,
            bool requestLogits)
        {
            ThrowIfDisposed();
            var batch = new LLamaBatch();
            var sequence = (LLamaSeqId)0;
            int lastLogitsIndex = -1;
            for (int index = 0; index < count; index++)
            {
                bool logits = requestLogits && index == count - 1;
                lastLogitsIndex = batch.Add(tokens[offset + index], position + index, sequence, logits);
            }

            var result = _context.Decode(batch);
            if (result != DecodeResult.Ok)
            {
                _logger.LogWarning("Learning prompt batch decode failed ({Result}).", result);
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed);
            }
            return lastLogitsIndex;
        }

        public int DecodeToken(LLamaToken token, int position)
        {
            ThrowIfDisposed();
            var batch = new LLamaBatch();
            int logitsIndex = batch.Add(token, position, (LLamaSeqId)0, logits: true);
            var result = _context.Decode(batch);
            if (result != DecodeResult.Ok)
            {
                _logger.LogWarning("Learning token decode failed ({Result}).", result);
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed);
            }
            return logitsIndex;
        }

        public bool IsEndOfGeneration(LLamaToken token)
        {
            ThrowIfDisposed();
            return token.IsEndOfGeneration(_weights.Vocab);
        }

        public byte[] TokenToBytes(LLamaToken token)
        {
            ThrowIfDisposed();
            byte[] buffer = new byte[128];
            uint written = _weights.NativeHandle.TokenToSpan(token, buffer, 0, false);
            if (written > 48_000)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.OutputTooLarge);
            if (written > buffer.Length)
            {
                buffer = new byte[(int)written];
                written = _weights.NativeHandle.TokenToSpan(token, buffer, 0, false);
            }
            if (written > buffer.Length)
                throw new LearningLocalRuntimeException(LearningLocalRuntimeFailure.DecodeFailed);
            return buffer.AsSpan(0, (int)written).ToArray();
        }

        public ILearningLocalSampler CreateSampler(LearningModelSamplingValues values)
        {
            ThrowIfDisposed();
            return new LLamaLearningLocalSampler(_context, values);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Context memory refers to the weights allocation, so free the context first.
            try
            {
                _context.Dispose();
            }
            finally
            {
                _weights.Dispose();
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class LLamaLearningLocalSampler : ILearningLocalSampler
    {
        private readonly LLamaContext _context;
        private readonly DefaultSamplingPipeline _pipeline;
        private bool _disposed;

        public LLamaLearningLocalSampler(LLamaContext context, LearningModelSamplingValues values)
        {
            _context = context;
            _pipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)values.Temperature,
                TopP = (float)values.TopP,
                TopK = values.TopK,
                RepeatPenalty = (float)values.RepetitionPenalty,
                MinP = 0,
                Seed = values.Seed,
            };
        }

        public LLamaToken Sample(int logitsIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipeline.Sample(_context, logitsIndex);
        }

        public void Accept(LLamaToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pipeline.Accept(token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pipeline.Dispose();
        }
    }
}
