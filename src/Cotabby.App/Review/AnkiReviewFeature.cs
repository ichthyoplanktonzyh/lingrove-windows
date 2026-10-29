using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;

namespace Cotabby.App.Review;

/// <summary>AppHost's single review composition/lifetime owner. Construction only wires shared
/// settings, optional borrowed legacy Store and the injected independent client/key store. UI actions stay on their caller's
/// context; shutdown closes admission, cancels requests, drains admitted actions, then disposes the
/// HTTP owner before the notebook closes. No startup probe, key read or model load occurs here.</summary>
internal sealed class AnkiReviewFeature : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HashSet<Task> _pending = [];
    private readonly IAnkiConnecting _client;
    private Task? _disposal;
    private readonly CancellationToken _token;
    internal AnkiReviewSettings Settings { get; }
    internal AnkiReviewController Controller { get; }
    internal AnkiReviewFeature(Func<AppSettings> current, Action persist, VocabularyStore? store,
        IAnkiConnecting client, IAnkiCredentialStore credentials, Func<string?>? speechConfigurationId = null, Action? activateExisting = null)
    {
        _client = client; _token = _cancellation.Token;
        Settings = new(current, persist, credentials);
        Controller = new(Settings, store, client, speechConfigurationId, activateExisting);
    }
    /// <summary>Real connection and preference owner without a legacy notebook. Canonical callers
    /// borrow this owner; no Store is constructed or opened here.</summary>
    internal static AnkiReviewFeature CreateConnectionSettings(Func<AppSettings> current, Action persist,
        IAnkiConnecting client, IAnkiCredentialStore credentials, Action? activateExisting = null) =>
        new(current, persist, null, client, credentials, activateExisting: activateExisting);
    internal Task RunAsync(Func<CancellationToken, Task> operation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposal is not null) return Task.FromException(new ObjectDisposedException(nameof(AnkiReviewFeature)));
            _pending.Add(completion.Task);
        }
        return Execute();
        async Task Execute()
        {
            // Reserve before invoking UI-owned work. A synchronous completion/failure must not
            // disappear between shutdown's admission check and its pending-work snapshot.
            try { await operation(_token); }
            finally
            {
                completion.TrySetResult(true);
                lock (_gate) _pending.Remove(completion.Task);
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        Task[] pending; TaskCompletionSource<bool> completion;
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _disposal = completion.Task;
            pending = _pending.ToArray();
        }
        _ = Drain(); return new(completion.Task);
        async Task Drain()
        {
            Exception? failure = null;
            try { await _cancellation.CancelAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
            // Completion reservations succeed even when an action failed: its actual Task
            // reports that error to the initiating UI. Shutdown still drains every action.
            await Task.WhenAll(pending).ConfigureAwait(false);
            try { (_client as IDisposable)?.Dispose(); } catch (Exception error) { failure ??= error; }
            _cancellation.Dispose();
            if (failure is null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }
    }
}
