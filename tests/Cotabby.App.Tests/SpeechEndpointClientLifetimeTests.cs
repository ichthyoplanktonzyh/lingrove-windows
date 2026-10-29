using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

public sealed partial class SpeechEndpointClientTests
{
    [Fact]
    public async Task RevokedReadingPermitBeforeSendDoesNotCallTransport()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int allowed = 1, sent = 0;
        var client = new SpeechEndpointClient(() => { entered.Set(); release.Wait(); return new PermitHandler(() => Interlocked.Increment(ref sent)); });
        var call = Task.Run(() => client.AudioAsync("fixture", Configuration(), null, admission: () => Volatile.Read(ref allowed) == 1));
        try { Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); Volatile.Write(ref allowed, 0); release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call); Assert.Equal(0, sent); }
        finally { release.Set(); }
    }
    private sealed class PermitHandler(Action send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { send(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleDeadlineResetsWithProgressWhileTheWholeResourceStillExpires(bool progress)
    {
        var clock = new ManualClock(); var stream = new ProgressStream();
        var client = new SpeechEndpointClient(() => new StreamHandler(stream), clock);
        var call = client.AudioAsync("fixture text", Configuration(), null);
        try
        {
            await stream.WaitForReadAsync();
            if (progress)
            {
                clock.Advance(TimeSpan.FromSeconds(89)); stream.ReleaseRead(); await stream.WaitForReadAsync();
                clock.Advance(TimeSpan.FromSeconds(30)); Assert.False(call.IsCompleted); // Total119 exceeds a single90-second deadline.
                stream.ReleaseRead(); await stream.WaitForReadAsync(); clock.Advance(TimeSpan.FromSeconds(1));
            }
            else clock.Advance(TimeSpan.FromSeconds(90));
            await Assert.ThrowsAsync<TimeoutException>(() => call); Assert.True(stream.Disposed);
        }
        finally { clock.Advance(TimeSpan.FromMinutes(5)); try { await call; } catch (TimeoutException) { } }
    }

    [Fact]
    public async Task CancellingOneUtteranceCannotCancelTheNextRequestsTransport()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = new ProgressStream(); var second = new FixtureStream(3); int calls = 0;
        var client = new SpeechEndpointClient(() => new StreamHandler(Interlocked.Increment(ref calls) == 1 ? first : second));
        var old = client.AudioAsync("old fixture", Configuration(), null, cancellation.Token);
        try
        {
            await first.WaitForReadAsync();
            byte[] audio = await client.AudioAsync("next fixture", Configuration(), null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { 90, 90, 90 }, audio); Assert.True(second.Disposed); Assert.False(old.IsCompleted);
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old); Assert.True(first.Disposed); Assert.Equal(2, calls);
        }
        finally { cancellation.Cancel(); try { await old; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task InvalidCredentialHeadersFailWithoutPublishingTheirValue()
    {
        var handler = new ScriptedHandler(System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        string key = "fixture-secret\r\ninjected: value";
        var error = await Assert.ThrowsAsync<SpeechException>(() => new SpeechEndpointClient(() => handler).AudioAsync("fixture", Configuration(), key));
        Assert.Equal(SpeechFailure.GenerationFailed, error.Failure); Assert.DoesNotContain("fixture-secret", error.ToString(), StringComparison.Ordinal);
        Assert.Null(handler.Request); Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task DefaultSocketsTransportDropsCookiesAndRefusesToFollowRedirects()
    {
        await using var server = new LoopbackSpeechServer(); var client = new SpeechEndpointClient();
        byte[] bytes = await client.AudioAsync("fixture cookie action", Configuration(server.BaseUrl + "/cookie"), "fixture-key", server.Cancellation).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        var error = await Assert.ThrowsAsync<SpeechException>(() => client.AudioAsync("fixture redirect action", Configuration(server.BaseUrl + "/redirect"), "fixture-key", server.Cancellation));
        Assert.Equal(SpeechFailure.EndpointStatus, error.Failure); Assert.Equal(302, error.Status);
        var requests = server.Requests.ToArray(); Assert.Equal(2, requests.Length);
        Assert.All(requests, request => Assert.DoesNotContain(request.Split("\r\n"), line => line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(requests, request => request.StartsWith("POST /stolen", StringComparison.Ordinal));
    }

    /// <summary>This local server observes the default SocketsHttpHandler rather than a fake
    /// handler's flags. Its only input is test-authored text/key, and disposal cancels/joins the
    /// bounded accept/read loop before the test returns. It never contacts a public endpoint.</summary>
    private sealed class LoopbackSpeechServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromSeconds(15));
        private readonly Task _work;
        internal string BaseUrl { get; }
        internal CancellationToken Cancellation => _cancellation.Token;
        internal ConcurrentQueue<string> Requests { get; } = new();
        internal ConcurrentQueue<JsonObject> Records { get; } = new();
        private readonly JsonElement _wire;
        internal LoopbackSpeechServer(JsonElement wire = default)
        {
            _wire = wire;
            _listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port; _work = RunAsync();
        }
        private async Task RunAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_cancellation.Token); await using var stream = connection.GetStream();
                    using var head = new MemoryStream(); byte[] one = new byte[1];
                    while (head.Length < 8192)
                    {
                        int n = await stream.ReadAsync(one, _cancellation.Token); if (n == 0) throw new IOException("Incomplete fixture HTTP header"); head.WriteByte(one[0]);
                        var data = head.GetBuffer(); int end = (int)head.Length;
                        if (end >= 4 && data[end - 4] == 13 && data[end - 3] == 10 && data[end - 2] == 13 && data[end - 1] == 10) break;
                    }
                    string headers = Encoding.ASCII.GetString(head.ToArray());
                    int length = int.Parse(headers.Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
                    if (length is < 0 or > 4096) throw new IOException("Fixture HTTP body exceeded its test bound");
                    byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, _cancellation.Token); Requests.Enqueue(headers);
                    if (_wire.ValueKind != JsonValueKind.Undefined)
                    {
                        string Header(string name) => headers.Split("\r\n").FirstOrDefault(line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? "";
                        Records.Enqueue(new() { ["url"] = "$LOOPBACK" + headers.Split(' ')[1], ["method"] = headers.Split(' ')[0], ["authorization"] = Header("Authorization"), ["accept"] = Header("Accept"), ["acceptEncoding"] = Header("Accept-Encoding"), ["contentType"] = Header("Content-Type"), ["body"] = JsonNode.Parse(body) });
                        byte[] decoded = new byte[_wire.GetProperty("decodedBytes").GetInt32()]; decoded.AsSpan().Fill(90);
                        using var compressed = new MemoryStream(); string encoding = Text(_wire, "encoding");
                        using (Stream encoder = encoding switch { "gzip" => new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true), "deflate" => new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true), _ => new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true) }) encoder.Write(decoded);
                        byte[] encoded = compressed.ToArray(); long declared = _wire.TryGetProperty("declared", out var declaredValue) ? declaredValue.GetInt64() : encoded.LongLength;
                        string wireHeaders = $"HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Encoding: {(encoding == "raw-deflate" ? "deflate" : encoding)}\r\nContent-Length: {declared}\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(wireHeaders), _cancellation.Token);
                        try { await stream.WriteAsync(encoded, _cancellation.Token); } catch (IOException) { /* The bounded client can close before the encoder's final bytes are sent. */ }
                        continue;
                    }
                    bool redirect = headers.StartsWith("POST /redirect/", StringComparison.Ordinal);
                    string response = redirect ? $"HTTP/1.1 302 Found\r\nLocation: {BaseUrl}/stolen\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nSet-Cookie: fixture=secret; Path=/\r\nContent-Length: 3\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _cancellation.Token);
                    if (!redirect) await stream.WriteAsync(new byte[] { 1, 2, 3 }, _cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            catch (SocketException) when (_cancellation.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { _cancellation.Cancel(); _listener.Stop(); try { await _work; } finally { _cancellation.Dispose(); } }
    }
}
