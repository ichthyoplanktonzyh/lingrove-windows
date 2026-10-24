using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>The shared controller borrows this independent speech producer. The composition
/// root supplies one local runtime and HTTP client; this owner routes the frozen explicit source
/// and forwards receiver backpressure without borrowing autocomplete or learning state. Endpoint
/// cancellation stays request-owned, while token cancel/shutdown retire only local helper work.</summary>
public sealed class SpeechAudioGenerator(LocalSpeechRuntime local, SpeechEndpointClient endpoint) : ISpeechAudioGenerating, IAsyncDisposable
{
    public async Task GenerateAsync(SpeechAudioRequest request, Func<SpeechAudioChunk, Task> receive, CancellationToken cancellationToken)
    {
        switch (request.Source)
        {
            case SpeechAudioSource.Local source:
                await local.StreamAsync(request, source.Configuration, chunk => receive(new SpeechAudioChunk.Pcm(chunk)), cancellationToken).ConfigureAwait(false);
                break;
            case SpeechAudioSource.Endpoint source:
                byte[] data = await endpoint.AudioAsync(request.Text, source.Configuration, source.ApiKey, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await receive(new SpeechAudioChunk.Encoded(data)).ConfigureAwait(false);
                break;
        }
    }
    public Task CancelAsync(SpeechPlaybackToken token) => local.CancelAsync(token);
    public Task ShutdownAsync() => local.ShutdownAsync();
    public ValueTask DisposeAsync() => local.DisposeAsync();
}
