using System.Net;
using System.Net.Http;
using System.IO;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Dictionaries;

/// <summary>Feature-owned fixed HTTPS provider. The caller grants each request explicitly;
/// no cookies, redirects, cache, pronunciation audio, or other application task is shared.</summary>
internal sealed class OnlineDictionaryClient : IDisposable
{
    private readonly HttpClient _client;
    internal OnlineDictionaryClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    internal OnlineDictionaryClient(HttpMessageHandler handler) => _client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    internal async Task<LearningDictionaryResult> LookupAsync(string query, CancellationToken token)
    {
        var url = DictionaryWebsitePolicy.Url(OnlineDictionaryPolicy.Template, query);
        if (url is null) return LearningDictionaryResult.NotFound;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); headersDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        HttpResponseMessage received;
        try { received = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("The online dictionary request timed out."); }
        using var response = received;
        if (response.StatusCode == HttpStatusCode.NotFound) return LearningDictionaryResult.NotFound;
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > OnlineDictionaryPolicy.MaximumBytes)
            throw new InvalidOperationException("The online dictionary is unavailable.");
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); byte[] chunk = new byte[4096];
            while (true)
            {
                int n = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false); if (n == 0) break;
                if (buffer.Length + n > OnlineDictionaryPolicy.MaximumBytes) throw new InvalidOperationException("The online dictionary response is too large.");
                buffer.Write(chunk, 0, n);
            }
            deadline.Token.ThrowIfCancellationRequested(); return OnlineDictionaryPolicy.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("The online dictionary request timed out."); }
    }
    public void Dispose() => _client.Dispose();
}
