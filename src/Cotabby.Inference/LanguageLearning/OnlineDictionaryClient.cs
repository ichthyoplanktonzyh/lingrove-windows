using System.Net;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.LanguageLearning;

/// <summary>
/// Application-owned, fixed dictionary transport, borrowed only after a session's explicit
/// online offer is accepted. Requests own their deadlines/responses; shutdown closes admission,
/// cancels and joins accepted work before releasing the private HTTP pool.
/// </summary>
public sealed class OnlineDictionaryClient : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private bool _closed;
    private Task? _dispose;

    public OnlineDictionaryClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    public OnlineDictionaryClient(HttpMessageHandler handler) => _client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    public Task<LearningDictionaryResult> LookupAsync(string query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<LearningDictionaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _work.Add(completion.Task);
        }
        _ = RunAcceptedAsync(query, token, completion);
        return completion.Task;
    }

    private async Task RunAcceptedAsync(string query, CancellationToken token, TaskCompletionSource<LearningDictionaryResult> completion)
    {
        try { completion.TrySetResult(await LookupCoreAsync(query, token).ConfigureAwait(false)); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { lock (_gate) _work.Remove(completion.Task); }
    }

    private async Task<LearningDictionaryResult> LookupCoreAsync(string query, CancellationToken token)
    {
        CheckCancellation(token);
        var url = DictionaryWebsitePolicy.Url(OnlineDictionaryPolicy.Template, query);
        if (url is null) return LearningDictionaryResult.NotFound;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
        try
        {
            CheckCancellation(token);
            HttpResponseMessage received;
            using (var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                headersDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                received = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
            }
            using var response = received;
            CheckCancellation(token);
            deadline.Token.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NotFound) return LearningDictionaryResult.NotFound;
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > OnlineDictionaryPolicy.MaximumBytes)
                throw new InvalidOperationException("The online dictionary is unavailable.");

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                CheckCancellation(token);
                int count = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false);
                CheckCancellation(token);
                if (count == 0) break;
                if (buffer.Length + count > OnlineDictionaryPolicy.MaximumBytes)
                    throw new InvalidOperationException("The online dictionary response is too large.");
                buffer.Write(chunk, 0, count);
            }
            deadline.Token.ThrowIfCancellationRequested();
            var result = OnlineDictionaryPolicy.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            CheckCancellation(token);
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        { throw new TimeoutException("The online dictionary request timed out."); }
    }

    private void CheckCancellation(CancellationToken token)
    { token.ThrowIfCancellationRequested(); _shutdown.Token.ThrowIfCancellationRequested(); }

    public ValueTask DisposeAsync()
    {
        Task[] work;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_dispose is not null) return new(_dispose);
            _closed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispose = completion.Task;
            work = _work.ToArray();
        }
        _ = DrainAsync(work, completion);
        return new(completion.Task);
    }

    private async Task DrainAsync(Task[] work, TaskCompletionSource completion)
    {
        try
        {
            try { await _shutdown.CancelAsync().ConfigureAwait(false); }
            finally
            {
                try { await Task.WhenAll(work).ConfigureAwait(false); }
                catch { /* Request callers own their individual outcomes. */ }
                finally { _client.Dispose(); _shutdown.Dispose(); }
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
