using Cotabby.Core.Speech;
using Cotabby.Win32.Speech;
using System.Runtime.Versioning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows10.0")]
public sealed class WindowsSystemSpeechProviderTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task LazyCatalogAndCancelledWorkerAreJoinedOnRepeatedDisposal() => dispatcher.RunAsync(async () =>
    {
        int catalogs = 0; var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var voice = new SpeechVoice("voice", "Voice", "en-US", SpeechVoiceQuality.Standard);
        var provider = new WindowsSystemSpeechProvider(() => { catalogs++; return [voice]; },
            (_, _, _, _, token) => { entered.TrySetResult(token); release.Wait(); });
        Assert.Equal(0, catalogs); Assert.Single(provider.RefreshVoices()); Assert.Equal(1, catalogs);
        var token = new SpeechPlaybackToken(); provider.Speak("fixture", voice, 1, token);
        var workerToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); provider.Stop(token);
        Assert.True(workerToken.IsCancellationRequested);
        Task first = provider.DisposeAsync().AsTask(), second = provider.DisposeAsync().AsTask();
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        release.Set(); await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Throws<ObjectDisposedException>(() => provider.RefreshVoices());
    });
    [Fact]
    public Task EncodedPlayerCopiesInputAndJoinsCancelledRendering() => dispatcher.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim(); byte[]? captured = null;
        var decoded = new WindowsSpeechDecodedAudio(8000, 1, 1, .000125, [new byte[4]]);
        var player = new WindowsEncodedSpeechPlayer((data, _) => { captured = data.ToArray(); return Task.FromResult(decoded); }, (_, _, _, token) => { entered.TrySetResult(token); release.Wait(); });
        byte[] bytes = [1, 2, 3]; player.Play(bytes, 1, new SpeechPlaybackToken()); Array.Fill(bytes, (byte)9);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(new byte[] { 1, 2, 3 }, captured);
        player.Stop(); Assert.True(token.IsCancellationRequested); var drain = player.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted);
        release.Set(); await drain.WaitAsync(TimeSpan.FromSeconds(10));
    });
    [Fact]
    public Task RecordingDeadlineDoesNotAbandonANativeWorker() => dispatcher.RunAsync(async () =>
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exporter = new WindowsSystemSpeechExporter((_, _, token) => { entered.TrySetResult(token); release.Wait(); return new SpeechExportAudio([1], "wav"); }, TimeSpan.FromMilliseconds(100));
        Task<SpeechExportAudio> pending = exporter.AudioAsync("test", "fixture", CancellationToken.None); var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 100 && !token.IsCancellationRequested; i++) await Task.Delay(10);
        Assert.True(token.IsCancellationRequested); Assert.False(pending.IsCompleted); release.Set();
        await Assert.ThrowsAsync<TimeoutException>(async () => await pending);
    });
    [Fact]
    public Task SystemRecordingWritesOnlyToBoundedStream() => dispatcher.RunAsync(async () =>
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_SYSTEM_SPEECH") != "1") return;
        await using var provider = new WindowsSystemSpeechProvider(); var voice = provider.RefreshVoices().FirstOrDefault();
        if (voice is null) return;
        var result = await new WindowsSystemSpeechExporter().AudioAsync("This is a test.", voice.Id, CancellationToken.None);
        Assert.Equal("wav", result.FileExtension); Assert.InRange(result.ByteCount, 45, 24000 * 2 * 90 + 44);
        Assert.True((await new WindowsSpeechAudioCodec().DurationAsync(result.CopyBytes(), CancellationToken.None)) > 0);
    });
}
