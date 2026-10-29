using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cotabby.Core.Speech;

namespace Cotabby.Win32.Speech;

/// <summary>One UI-owned SAPI adapter. Each explicit utterance owns its own COM apartment;
/// callbacks carry its token to the captured UI context. Construction does not enumerate voices
/// or open an audio device. Shutdown cancels and joins every admitted worker.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemSpeechProvider : ISpeechProviding, IAsyncDisposable
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current
        ?? throw new InvalidOperationException("Speech requires a UI context.");
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private readonly Func<IReadOnlyList<SpeechVoice>> _catalog;
    private readonly Action<string, string, double, SpeechPlaybackToken, CancellationToken> _speak;
    private CancellationTokenSource? _active;
    private SpeechPlaybackToken? _token;
    private bool _closed;
    private Task? _disposal;
    public Action<SpeechProviderEvent>? EventHandler { get; set; }

    public WindowsSystemSpeechProvider() { _catalog = Catalog; _speak = SpeakCore; }
    internal WindowsSystemSpeechProvider(Func<IReadOnlyList<SpeechVoice>> catalog,
        Action<string, string, double, SpeechPlaybackToken, CancellationToken> speak)
    { _catalog = catalog; _speak = speak; }

    public IReadOnlyList<SpeechVoice> RefreshVoices()
    {
        lock (_gate) if (_closed) throw new ObjectDisposedException(nameof(WindowsSystemSpeechProvider));
        return _catalog();
    }

    public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token)
    {
        CancellationTokenSource cancellation = new();
        lock (_gate)
        {
            if (_closed) { cancellation.Dispose(); throw new ObjectDisposedException(nameof(WindowsSystemSpeechProvider)); }
            _active?.Cancel(); _active = cancellation; _token = token;
            Task task = WindowsSpeechMta.RunAsync(() => { _speak(text, voice.Id, rateMultiplier, token, cancellation.Token); return true; }, CancellationToken.None);
            _work.Add(task);
            _ = task.ContinueWith(t => { if (t.IsFaulted) { _ = t.Exception; Post(new SpeechProviderEvent.Failed(token, "Unable to play the selected system voice.")); } lock (_gate) { _work.Remove(t); if (ReferenceEquals(_active, cancellation)) { _active = null; _token = null; } } cancellation.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public void Stop(SpeechPlaybackToken token)
    { lock (_gate) { if (_token == token) _active?.Cancel(); } }

    private void Post(SpeechProviderEvent value) => _context.Post(_ =>
    {
        lock (_gate) if (_closed) return;
        EventHandler?.Invoke(value);
    }, null);

    private static IReadOnlyList<SpeechVoice> Catalog()
    {
        dynamic? synth = null, voices = null;
        var result = new List<SpeechVoice>();
        try
        {
            synth = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice", true)!)!;
            voices = synth.GetVoices(); int count = voices.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic? item = null;
                try
                {
                    item = voices.Item(i); string id = item.Id, name = item.GetDescription();
                    string raw = item.GetAttribute("Language"); string first = raw.Split(';')[0];
                    if (int.TryParse(first, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int lcid))
                    {
                        try { string language = CultureInfo.GetCultureInfo(lcid).Name; if (language.Length > 0) result.Add(new(id, name, language, SpeechVoiceQuality.Standard)); }
                        catch (CultureNotFoundException) { }
                    }
                }
                catch (COMException) { /* A malformed voice does not hide other installed voices. */ }
                finally { Release(item); }
            }
            return result.AsReadOnly();
        }
        finally { Release(voices); Release(synth); }
    }

    private void SpeakCore(string text, string voiceId, double rate, SpeechPlaybackToken token, CancellationToken cancellation)
    {
        dynamic? synth = null, voices = null, selected = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            synth = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice", true)!)!;
            voices = synth.GetVoices(); int count = voices.Count;
            for (int i = 0; i < count; i++) { dynamic item = voices.Item(i); if (string.Equals((string)item.Id, voiceId, StringComparison.Ordinal)) { selected = item; break; } Release(item); }
            if (selected is null) throw new InvalidOperationException("The selected speech voice is no longer available.");
            synth.Voice = selected; synth.Rate = Math.Clamp((int)Math.Round(Math.Log2(Math.Clamp(rate, .5, 1.5)) * 10), -10, 10);
            cancellation.ThrowIfCancellationRequested(); synth.Speak(text, 17); Post(new SpeechProviderEvent.Started(token));
            while (!(bool)synth.WaitUntilDone(50)) { if (cancellation.IsCancellationRequested) { synth.Speak("", 3); throw new OperationCanceledException(cancellation); } }
            cancellation.ThrowIfCancellationRequested(); Post(new SpeechProviderEvent.Finished(token));
        }
        catch (OperationCanceledException) { Post(new SpeechProviderEvent.Cancelled(token)); }
        catch (Exception) { Post(new SpeechProviderEvent.Failed(token, "Unable to play the selected system voice.")); }
        finally { Release(selected); Release(voices); Release(synth); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _closed = true; _active?.Cancel(); _disposal = Task.WhenAll(_work.ToArray()); return new(_disposal);
        }
    }
}
