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
    private sealed record Pending(string Text, string VoiceId, double Rate, SpeechPlaybackToken Token, Func<bool>? Admission);
    private Pending? _pending;
    private Task? _pump;
    private readonly Func<IReadOnlyList<SpeechVoice>> _catalog;
    private readonly Action<string, string, double, SpeechPlaybackToken, CancellationToken, Func<bool>?> _speak;
    private CancellationTokenSource? _active;
    private SpeechPlaybackToken? _token;
    private bool _closed;
    private Task? _disposal;
    public Action<SpeechProviderEvent>? EventHandler { get; set; }

    public WindowsSystemSpeechProvider() { _catalog = Catalog; _speak = SpeakCore; }
    internal WindowsSystemSpeechProvider(Func<IReadOnlyList<SpeechVoice>> catalog,
        Action<string, string, double, SpeechPlaybackToken, CancellationToken, Func<bool>?> speak)
    { _catalog = catalog; _speak = speak; }

    public IReadOnlyList<SpeechVoice> RefreshVoices()
    {
        lock (_gate) if (_closed) throw new ObjectDisposedException(nameof(WindowsSystemSpeechProvider));
        return _catalog();
    }

    public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token) => Speak(text, voice, rateMultiplier, token, null);
    public void Speak(string text, SpeechVoice voice, double rateMultiplier, SpeechPlaybackToken token, Func<bool>? admission)
    {
        lock (_gate)
        {
            if (_closed) throw new ObjectDisposedException(nameof(WindowsSystemSpeechProvider));
            _active?.Cancel(); _pending = new(text, voice.Id, rateMultiplier, token, admission); _token = token;
            _pump ??= Task.Run(PumpAsync);
        }
    }

    public void Stop(SpeechPlaybackToken token)
    { lock (_gate) { if (_token == token) { _pending = null; _active?.Cancel(); _token = null; } } }

    private async Task PumpAsync()
    {
        while (true)
        {
            Pending item; CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_closed || _pending is null) { _pump = null; return; }
                item = _pending; _pending = null; _active = cancellation = new();
            }
            try
            {
                await WindowsSpeechMta.RunAsync(() => { SpeechAdmission.Require(item.Admission); _speak(item.Text, item.VoiceId, item.Rate, item.Token, cancellation.Token, item.Admission); return true; }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { Post(new SpeechProviderEvent.Cancelled(item.Token)); }
            catch { Post(new SpeechProviderEvent.Failed(item.Token, "Unable to play the selected system voice.")); }
            finally { lock (_gate) { if (ReferenceEquals(_active, cancellation)) _active = null; if (_token == item.Token && _pending is null) _token = null; } cancellation.Dispose(); }
        }
    }

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

    private void SpeakCore(string text, string voiceId, double rate, SpeechPlaybackToken token, CancellationToken cancellation, Func<bool>? admission)
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
            cancellation.ThrowIfCancellationRequested(); SpeechAdmission.Require(admission); synth.Speak(text, 17); Post(new SpeechProviderEvent.Started(token));
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
            _closed = true; _pending = null; _active?.Cancel(); _disposal = _pump ?? Task.CompletedTask; return new(_disposal);
        }
    }
}
