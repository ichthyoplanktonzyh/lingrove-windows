using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
using Xunit;

namespace Cotabby.Tests.Speech;

/// <summary>Isolated test files and a real protocol child. The portable factory is a test effect,
/// whereas native Windows tests inject the production CreateProcess/job adapter into this owner.
/// Cleanup always reaps children before deleting the executable/model-path fixture directory.</summary>
internal sealed class SpeechRuntimeFixture : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "lingrove speech 测试 " + Guid.NewGuid().ToString("N"));
    internal string Executable { get; }
    internal SpeechLocalConfiguration Configuration => new(new Uri(Path.Combine(Root, "model 模型.gguf")), new Uri(Path.Combine(Root, "decoder 解码.gguf")));
    internal SpeechTestPressure Pressure { get; } = new();
    internal SpeechTestProcessFactory Factory { get; } = new();
    internal LocalSpeechRuntime Runtime { get; }
    internal SpeechRuntimeFixture(Func<SpeechRuntimeFixture, ISpeechHelperProcessFactory>? factory = null,
        TimeSpan? idle = null, TimeSpan? operation = null, Func<SpeechRuntimeEvent, Task>? events = null, TimeProvider? clock = null)
    {
        Directory.CreateDirectory(Root);
        // Copy all apphost dependencies to exercise native launch from a space/Unicode path.
        string source = Path.Combine(AppContext.BaseDirectory, "SpeechTestHelper"), target = Path.Combine(Root, "helper 程序");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        Executable = Path.Combine(target, "Cotabby.Speech.TestHelper" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Executable, File.GetUnixFileMode(Executable) | UnixFileMode.UserExecute);
        Runtime = new(factory?.Invoke(this) ?? Factory, Pressure, Executable, idle, operation, events, clock);
    }
    internal SpeechAudioRequest Request(string text, LearningModelSamplingOverrides? sampling = null, LearningLanguage language = LearningLanguage.English, SpeechLocalConfiguration? configuration = null) =>
        new(new(), text, language, new SpeechAudioSource.Local(configuration ?? Configuration), sampling);
    internal async Task<byte[]> CollectAsync(SpeechAudioRequest request, SpeechLocalConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        using var bytes = new MemoryStream();
        await Runtime.StreamAsync(request, configuration ?? Configuration, chunk => { bytes.Write(chunk.CopyBytes()); return Task.CompletedTask; }, cancellationToken);
        return bytes.ToArray();
    }
    internal string[] Lines(string file) => File.Exists(Path.Combine(Root, file)) ? File.ReadAllLines(Path.Combine(Root, file)) : [];
    internal JsonElement[] Parameters() => Lines("parameters").Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
    internal async Task WaitForRequestAsync(string text) => await WaitAsync(() => Lines("requests").Contains(text));
    internal static async Task WaitAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }
    public async ValueTask DisposeAsync()
    {
        try { await Runtime.DisposeAsync(); }
        finally { await Factory.DisposeAsync(); Directory.Delete(Root, recursive: true); }
    }
}
internal sealed class SpeechTestPressure : ISpeechMemoryPressureSource
{
    private Action? _warning;
    internal int Subscriptions, Disposals;
    public IDisposable Observe(Action warning) { _warning = warning; Subscriptions++; return new Binding(this); }
    internal void Warn() => _warning?.Invoke();
    private sealed class Binding(SpeechTestPressure owner) : IDisposable
    { public void Dispose() { owner._warning = null; owner.Disposals++; } }
}
internal sealed class SpeechTestProcessFactory : ISpeechHelperProcessFactory, IAsyncDisposable
{
    internal ConcurrentQueue<SpeechTestProcess> Processes { get; } = new();
    public Task<ISpeechHelperProcess> StartAsync(string executable, SpeechLocalConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "--model", configuration.ModelUrl.LocalPath, "--mmproj", configuration.ProjectionUrl.LocalPath, "--parent-pid", Environment.ProcessId.ToString() }) info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new IOException("The test helper did not start.");
        var owned = new SpeechTestProcess(process); Processes.Enqueue(owned); return Task.FromResult<ISpeechHelperProcess>(owned);
    }
    public async ValueTask DisposeAsync()
    { foreach (var process in Processes) { await process.StopAndReapAsync(); await process.DisposeAsync(); } }
}
internal sealed class SpeechTestProcess(Process process) : ISpeechHelperProcess
{
    private readonly object _gate = new();
    private Task<bool>? _stop;
    private bool _disposed;
    private readonly Task _errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    internal TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _id = process.Id;
    public int Id => _id;
    public bool HasExited => _disposed || process.HasExited;
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public Task<bool> StopAndReapAsync()
    {
        lock (_gate) return _stop ??= Task.Run(async () =>
        {
            Stopping.TrySetResult(); if (_disposed) return true;
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            return process.HasExited;
        });
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; Assert.True(process.HasExited); _disposed = true; }
        await _errors; process.Dispose();
    }
}
internal sealed class SpeechTestGate
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task WaitAsync() { Entered.TrySetResult(); return _release.Task; }
    internal void Release() => _release.TrySetResult();
}
