using System.Net.Http;
using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Learning;

/// <summary>AppHost owns this role's HTTP pool/credential boundary once. The existing
/// explanation controller owns generation lifetimes; settings work is admitted here so its
/// UI continuations and metadata requests finish before that shared pool is retired.</summary>
internal sealed class LearningExplanationEndpointFeature : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly OpenAICompatibleApiClient _metadata;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _work = [];
    private Task? _retirement;
    private bool _closed;
    internal IOpenAICompatibleCredentialStore Credentials { get; }

    internal LearningExplanationEndpointFeature(Dispatcher dispatcher, IOpenAICompatibleCredentialStore credentials, HttpClient? http = null)
    {
        _dispatcher = dispatcher; Credentials = credentials;
        _ownsHttp = http is null; _http = http ?? LearningEndpointInstructionEngine.CreateHttpClient();
        _metadata = OpenAICompatibleApiClient.Borrowing(_http);
    }
    internal ILearningExplanationEngine ExplanationEngine(LearningModelSnapshot profile)
    { lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); return new LearningEndpointExplanationEngine(profile, Credentials, _http); } }
    internal ILearningInstructionEngine InstructionEngine(LearningModelSnapshot profile)
    { lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); return new LearningEndpointInstructionEngine(profile, Credentials, _http); } }

    /// <summary>Only an explicit Save/Clear action may read or change the stored key.</summary>
    internal bool SaveCredential(string? value)
    {
        _dispatcher.VerifyAccess(); lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        string? normalized = value is null ? null : Cotabby.Core.Text.FoundationText.TrimWhitespaceAndNewlines(value);
        if (normalized?.Length == 0) normalized = null;
        if (normalized is not null && (normalized.Length > 8192 || normalized.Any(char.IsControl)))
            throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable);
        try
        {
            if (string.Equals(Credentials.ReadApiKey(), normalized, StringComparison.Ordinal)) return false;
            if (normalized is null) Credentials.DeleteApiKey(); else Credentials.SaveApiKey(normalized);
            return true;
        }
        catch { throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable); }
    }

    internal Task RunAsync(Func<CancellationToken, Task> operation)
    {
        _dispatcher.VerifyAccess();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _work.Add(completion.Task);
        }
        _ = CompleteAsync();
        return completion.Task;
        async Task CompleteAsync()
        {
            try { await operation(_shutdown.Token); completion.TrySetResult(); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (_gate) _work.Remove(completion.Task); }
        }
    }

    internal Task<IReadOnlyList<OpenAICompatibleModelOption>> FetchModelsAsync(LearningModelSnapshot profile, CancellationToken caller)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var task = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _shutdown.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (profile.Backend != LearningModelBackend.Endpoint)
                    throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.NotConfigured);
                if (profile.BaseUrl.Length > 4096)
                    throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.InvalidRequest);
                var configuration = new OpenAICompatibleEndpointConfiguration(profile.BaseUrl, profile.EndpointModel, OpenAICompatibleApiMode.ChatCompletions);
                string? key;
                try { key = Credentials.ReadApiKey(); }
                catch { throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable); }
                linked.Token.ThrowIfCancellationRequested();
                if (key is not null && (key.Length > 8192 || key.Any(char.IsControl)))
                    throw new LearningTranslationEndpointException(LearningTranslationEndpointFailure.CredentialUnavailable);
                return await _metadata.FetchModelsAsync(configuration, key, linked.Token).ConfigureAwait(false);
            });
            _work.Add(task);
            _ = task.ContinueWith(_ => { lock (_gate) _work.Remove(task); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_retirement is not null) return new(_retirement);
            _closed = true;
            return new(_retirement = DrainAsync(_work.ToArray()));
        }
    }
    private async Task DrainAsync(Task[] work)
    {
        Task cancellation;
        try { cancellation = _shutdown.CancelAsync(); } catch { cancellation = Task.CompletedTask; }
        try
        {
            try { await Task.WhenAll(work).ConfigureAwait(false); } catch { }
            try { await cancellation.ConfigureAwait(false); } catch { }
        }
        finally { _metadata.Dispose(); if (_ownsHttp) _http.Dispose(); _shutdown.Dispose(); }
    }
}
