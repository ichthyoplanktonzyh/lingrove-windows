using System.Net;
using System.Text;
using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Win32.Review;

namespace Cotabby.App.Tests;

public sealed class AnkiConnectBoundaryTests
{
    private static AnkiConnection Connection => new("http://127.0.0.1:8765", "fixture-secret-key");
    private static IReadOnlyDictionary<string, AnkiValue> Parameters => new Dictionary<string, AnkiValue>();
    [Fact]
    public void DefaultHandlerCannotRedirectPersistCookiesUseProxyOrDefaultCredentials()
    {
        using var handler = AnkiConnectClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect); Assert.False(handler.UseCookies); Assert.False(handler.UseProxy); Assert.False(handler.UseDefaultCredentials);
        Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli, handler.AutomaticDecompression);
        Assert.DoesNotContain("fixture-secret-key", Connection.ToString());
    }
    [Fact]
    public async Task RejectedEndpointNeverReachesHandler()
    {
        using var handler = new Handler(new TrackedStream("unused")); using var client = new AnkiConnectClient(handler);
        var error = await Assert.ThrowsAsync<AnkiReviewException>(() => client.PerformAsync("version", Parameters, new("http://127.1:8765", "fixture-secret-key")));
        Assert.Equal(AnkiWirePolicy.InvalidResponse, error.Message); Assert.Equal(0, handler.Requests);
    }
    [Fact]
    public async Task ApprovedSpellingsDispatchOnlyToNormalizedLocalTarget()
    {
        foreach (var (input, expected) in new[]
        {
            (" http://LOCALHOST:08765/\u200b", "http://localhost:8765"),
            ("http://%6cocalhost:", "http://localhost"),
            ("http://[::1]:8765/", "http://[::1]:8765"),
        })
        {
            using var handler = new Handler(new TrackedStream("{\"error\":null,\"result\":6}")); using var client = new AnkiConnectClient(handler);
            await client.PerformAsync("version", Parameters, new(input, "fixture-key"));
            Assert.Equal(expected, handler.Target!.OriginalString); Assert.True(handler.Target.IsLoopback); Assert.Equal(1, handler.Requests);
        }
    }
    [Theory, InlineData(HttpStatusCode.Redirect), InlineData(HttpStatusCode.Unauthorized)]
    public async Task StatusFailureDisposesContentWithoutReadingOrRetrying(HttpStatusCode status)
    {
        var stream = new TrackedStream("fixture-secret-body"); using var handler = new Handler(stream) { Status = status };
        using (var client = new AnkiConnectClient(handler))
        {
            var error = await Assert.ThrowsAsync<AnkiReviewException>(() => client.PerformAsync("version", Parameters, Connection));
            Assert.Equal(AnkiWirePolicy.InvalidResponse, error.Message); Assert.Null(error.InnerException);
            Assert.Equal(1, handler.Requests); Assert.Equal(0, stream.BytesRead); Assert.True(stream.Disposed);
        }
        Assert.True(handler.Disposed);
    }
    [Fact]
    public async Task OversizedResponseReadsAtMostOneBytePastLimitAndDisposes()
    {
        var stream = new TrackedStream(new string(' ', AnkiWirePolicy.MaximumResponseBytes + 5000));
        using var handler = new Handler(stream); using var client = new AnkiConnectClient(handler);
        var error = await Assert.ThrowsAsync<AnkiReviewException>(() => client.PerformAsync("version", Parameters, Connection));
        Assert.Equal(AnkiWirePolicy.InvalidResponse, error.Message); Assert.Equal(AnkiWirePolicy.MaximumResponseBytes + 1, stream.BytesRead); Assert.True(stream.Disposed);
    }
    [Fact]
    public async Task CallerCancellationWhileReadingRemainsCancellationAndDisposes()
    {
        var stream = new TrackedStream("") { BlockReads = true }; using var handler = new Handler(stream); using var client = new AnkiConnectClient(handler);
        using var cancellation = new CancellationTokenSource();
        var work = client.PerformAsync("version", Parameters, Connection, cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.True(stream.Disposed); Assert.Equal(1, handler.Requests);
    }
    [Fact]
    public async Task OwnDeadlineWhileReadingHasFixedConnectMessageAndDisposes()
    {
        var stream = new TrackedStream("") { BlockReads = true }; using var handler = new Handler(stream);
        var clock = new ManualClock(); using var client = new AnkiConnectClient(handler, TimeSpan.FromSeconds(10), clock);
        var work = client.PerformAsync("version", Parameters, Connection);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Advance(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<AnkiReviewException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AnkiWirePolicy.CouldNotConnect, error.Message); Assert.Null(error.InnerException); Assert.True(stream.Disposed); Assert.Equal(1, handler.Requests);
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task HeaderWaitObservesOwnedTimeoutAndCallerCancellation(bool callerCancels)
    {
        using var handler = new HeaderGateHandler(); var clock = new ManualClock();
        using var client = new AnkiConnectClient(handler, TimeSpan.FromSeconds(10), clock);
        using var cancellation = new CancellationTokenSource();
        var work = client.PerformAsync("version", Parameters, Connection, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels) cancellation.Cancel(); else clock.Advance(TimeSpan.FromSeconds(10));
        if (callerCancels)
        {
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<AnkiReviewException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(AnkiWirePolicy.CouldNotConnect, error.Message); Assert.Null(error.InnerException);
        }
        Assert.True(handler.ObservedCancellation); Assert.Equal(1, handler.Requests);
    }
    [Fact]
    public async Task ReadFailureNeverRetainsTransportTextOrInnerError()
    {
        var stream = new TrackedStream("") { FailReads = true }; using var handler = new Handler(stream); using var client = new AnkiConnectClient(handler);
        var error = await Assert.ThrowsAsync<AnkiReviewException>(() => client.PerformAsync("version", Parameters, Connection));
        Assert.Equal(AnkiWirePolicy.CouldNotConnect, error.Message); Assert.DoesNotContain("fixture-secret", error.ToString()); Assert.Null(error.InnerException); Assert.True(stream.Disposed);
    }
    [Fact]
    public async Task SuccessfulResponseDisposesBodyAndOwnedHandler()
    {
        var stream = new TrackedStream("{\"error\":null,\"result\":6}"); using var handler = new Handler(stream);
        using (var client = new AnkiConnectClient(handler))
        {
            Assert.Equal(new AnkiValue.Number(6), await client.PerformAsync("version", Parameters, Connection));
            Assert.True(stream.Disposed); Assert.False(handler.Disposed); Assert.Equal(1, handler.Requests);
        }
        Assert.True(handler.Disposed);
    }
    [Fact]
    public async Task RequestValidationRunsBeforeTransmissionEvenForOversizeWithInvalidLateValue()
    {
        using var handler = new Handler(new TrackedStream("unused")); using var client = new AnkiConnectClient(handler);
        var values = new Dictionary<string, AnkiValue>
        {
            ["large"] = new AnkiValue.Text(new string('a', AnkiWirePolicy.MaximumRequestBytes + 1)), ["nan"] = new AnkiValue.Number(double.NaN),
        };
        await Assert.ThrowsAsync<JsonException>(() => client.PerformAsync("version", values, Connection)); Assert.Equal(0, handler.Requests);
        values.Remove("nan"); var error = await Assert.ThrowsAsync<AnkiReviewException>(() => client.PerformAsync("version", values, Connection));
        Assert.Equal(AnkiWirePolicy.RequestTooLarge, error.Message); Assert.Equal(0, handler.Requests);
    }
    [Fact]
    public void RequestEncodingRejectsUnpairedUtf16AndRetainsObservedEncodingDepth()
    {
        Assert.Throws<JsonException>(() => AnkiWirePolicy.EncodeBody(new Dictionary<string, AnkiValue> { ["payload"] = new AnkiValue.Text("\ud800") }));
        AnkiValue value = AnkiValue.Null;
        for (int i = 0; i < 510; i++) value = new AnkiValue.Sequence([value]);
        Assert.NotEmpty(AnkiWirePolicy.EncodeBody(new Dictionary<string, AnkiValue> { ["payload"] = value }));
        value = new AnkiValue.Sequence([value]);
        Assert.Throws<JsonException>(() => AnkiWirePolicy.EncodeBody(new Dictionary<string, AnkiValue> { ["payload"] = value }));
    }
    [Fact]
    public async Task BodyProgressResetsIdleTimeoutBeyondTotalTenSeconds()
    {
        var stream = new ProgressStream(); using var handler = new StreamHandler(stream); var clock = new ManualClock();
        using var client = new AnkiConnectClient(handler, TimeSpan.FromSeconds(10), clock);
        var work = client.PerformAsync("version", Parameters, Connection);
        for (int step = 0; step < 3; step++)
        {
            await stream.WaitForReadAsync(); clock.Advance(TimeSpan.FromSeconds(9)); Assert.False(work.IsCompleted); stream.ReleaseRead();
        }
        await stream.WaitForReadAsync(); clock.Advance(TimeSpan.FromSeconds(9)); Assert.False(work.IsCompleted); stream.ReleaseRead();
        Assert.Equal(new AnkiValue.Number(6), await work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
    }
    private sealed class Handler(TrackedStream stream) : HttpMessageHandler
    {
        public Uri? Target { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; Target = request.RequestUri; return Task.FromResult(new HttpResponseMessage(Status) { Content = new StreamContent(stream) }); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class TrackedStream(string text) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public bool BlockReads { get; init; }
        public bool FailReads { get; init; }
        public bool Disposed { get; private set; }
        public int BytesRead { get; private set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            if (BlockReads) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailReads) throw new IOException("fixture-secret-body/key");
            int count = await base.ReadAsync(buffer, cancellationToken); BytesRead += count; return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

// Test-only virtual time makes timeout/progress ordering deterministic under a busy CI runner.
// The production CancellationTokenSource owns the actual timer and resets it through CancelAfter.
internal sealed class ManualClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<Timer> _timers = [];
    private TimeSpan _elapsed;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state); lock (_gate) _timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    public void Advance(TimeSpan interval)
    {
        List<Timer> ready;
        lock (_gate) { _elapsed += interval; ready = _timers.Where(t => t.Due <= _elapsed).ToList(); foreach (var t in ready) t.Due = TimeSpan.MaxValue; }
        foreach (var timer in ready) timer.Callback(timer.State);
    }
    private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public TimeSpan Due { get; set; } = TimeSpan.MaxValue;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        { lock (owner._gate) Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner._elapsed + dueTime; return true; }
        public void Dispose() { lock (owner._gate) Due = TimeSpan.MaxValue; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
internal sealed class StreamHandler(Stream stream) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
}
internal sealed class ProgressStream : Stream
{
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes("{\"error\":null,\"result\":6}");
    private readonly SemaphoreSlim _started = new(0), _release = new(0);
    private int _step;
    public bool Disposed { get; private set; }
    public async Task WaitForReadAsync() => Assert.True(await _started.WaitAsync(TimeSpan.FromSeconds(5)));
    public void ReleaseRead() => _release.Release();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _started.Release(); await _release.WaitAsync(cancellationToken);
        if (_step >= 3) return 0;
        int start = _step, count = _step < 2 ? 1 : _bytes.Length - 2; _bytes.AsMemory(start, count).CopyTo(buffer); _step++; return count;
    }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal sealed class HeaderGateHandler : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ObservedCancellation { get; private set; }
    public int Requests { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++; Started.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException("Gate unexpectedly released"); }
        catch (OperationCanceledException) { ObservedCancellation = true; throw; }
    }
}
