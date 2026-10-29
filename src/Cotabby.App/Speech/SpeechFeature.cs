using System.IO;
using System.Runtime.Versioning;
using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.Core.Speech;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Inference.Speech;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Speech;

/// <summary>One application-owned speech runtime. Construction projects existing preferences
/// (including the settings owner's legacy profile upgrade); voice enumeration, media devices,
/// model discovery, subprocesses and HTTP start only after explicit actions.
/// Cards and settings borrow its controller; shutdown joins controller work and native owners.</summary>
[SupportedOSPlatform("windows10.0")]
internal sealed class SpeechFeature : IAsyncDisposable
{
    private readonly WindowsSystemSpeechProvider _system;
    private readonly WindowsEncodedSpeechPlayer _encoded;
    private readonly WindowsSpeechPCMRenderer _renderer;
    private readonly SpeechPCMPlayer _pcm;
    private readonly SpeechAudioGenerator _generator;
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private SpeechModelFiles? _models;
    private Task? _retirement;
    internal LearningSpeechController Controller { get; }
    internal string ModelDirectory { get; }
    internal static string DefaultModelDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cotabby", "SpeechModels");

    internal SpeechFeature(Func<AppSettings> current, Action persist, IOpenAICompatibleCredentialStore credentials, string modelDirectory)
        : this(current, persist, credentials, modelDirectory, new WindowsSystemSpeechExporter()) { }

    internal SpeechFeature(Func<AppSettings> current, Action persist, IOpenAICompatibleCredentialStore credentials,
        string modelDirectory, ISystemSpeechAudioExporting systemExporter)
    {
        _dispatcher = Dispatcher.CurrentDispatcher; ModelDirectory = modelDirectory;
        var settings = new LearningSpeechSettings(current, persist, credentials);
        var telemetry = new SpeechTelemetry((_, _, _) => { });
        _system = new(); _encoded = new(); _renderer = WindowsSpeechRendering.CreateRenderer(); _pcm = new(_renderer);
        var local = new LocalSpeechRuntime(new WindowsSpeechProcessFactory(), new WindowsSpeechMemoryPressure(),
            events: value => _dispatcher.InvokeAsync(() => telemetry.Receive(value)).Task);
        _generator = new(local, new SpeechEndpointClient());
        Controller = new(settings, telemetry, _system, _generator, _encoded, new WindowsSpeechPCMInputAdapter(_pcm),
            systemExporter, new WindowsSpeechAudioCodec(), _ => new Dictionary<string, double>(),
            StringComparer.CurrentCulture, modelDirectory, () => _models ??= new(ModelDirectory, _dispatcher));
    }
    internal SpeechModelFiles Models => (SpeechModelFiles)Controller.ModelDownloads;
    public ValueTask DisposeAsync() { lock (_gate) return new(_retirement ??= RetireAsync()); }
    private async Task RetireAsync()
    {
        if (!_dispatcher.CheckAccess()) { await (await _dispatcher.InvokeAsync(RetireAsync)); return; }
        try { try { if (_models is not null) await _models.DisposeAsync(); } finally { await Controller.DisposeAsync(); } }
        finally
        {
            try { await _pcm.DrainAsync(); }
            finally
            {
                try { await _system.DisposeAsync(); }
                finally { try { await _encoded.DisposeAsync(); } finally { try { await _renderer.DisposeAsync(); } finally { await _generator.DisposeAsync(); } } }
            }
        }
    }
}
