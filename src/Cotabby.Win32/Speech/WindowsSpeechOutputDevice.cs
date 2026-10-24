namespace Cotabby.Win32.Speech;

/// <summary>The renderer borrows a factory for one independently owned output graph. Only PCM,
/// playback epochs and authored-frame cursors cross this seam. The concrete WASAPI device owns
/// its COM/event/render thread; DSP must never run there. Callbacks only queue renderer work.</summary>
internal interface IWindowsSpeechOutputDeviceFactory
{
    Task<IWindowsSpeechOutputDevice> CreateAsync(int sampleRate, Action<ulong, long> progressed, Action invalidated, CancellationToken cancellationToken);
}

/// <summary>Enqueue transfers detached packet ownership to the device. Progress counts authored
/// frames actually reached by its raw stream clock, excluding inserted generation-gap silence.
/// Reset retires queued packets before discarding native output, and changes the callback epoch.
/// Device loss marks IsValid false before publishing any invalidation callback. Accepted commands
/// and native cleanup remain owned until they complete; callers cannot abandon them with WaitAsync.</summary>
internal interface IWindowsSpeechOutputDevice : IAsyncDisposable
{
    bool IsValid { get; }
    Task ResetAsync(ulong epoch);
    Task EnqueueAsync(IReadOnlyList<byte[]> packets, ulong epoch);
    Task StartAsync(ulong epoch);
}
