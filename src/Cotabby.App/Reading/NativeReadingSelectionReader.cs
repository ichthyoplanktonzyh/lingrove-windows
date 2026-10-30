using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;
using System.Runtime.Versioning;

namespace Cotabby.App.Reading;

/// <summary>Adapts the shared native read-only owner without exposing its retained UIA range.</summary>
[SupportedOSPlatform("windows8.1")]
internal sealed class NativeReadingSelectionReader(Win32ReadingSelectionReader reader) : IReadingSelectionReader, IReadingPointerReader, IReadingPendingKeyOwner
{
    public void ArmPendingActivationKey(int virtualKey) => reader.ArmPendingActivationKey(virtualKey);
    public async Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
    {
        var lease = await reader.CaptureAsync(token, initiatingVirtualKey).ConfigureAwait(false);
        return lease is null ? null : new Adapter(lease);
    }
    public async Task<IReadingSelectionLease?> CapturePointerAsync(int x, int y, CancellationToken token, int? initiatingVirtualKey = null)
    {
        var lease = await reader.CapturePointerAsync(x, y, token, initiatingVirtualKey).ConfigureAwait(false);
        return lease is null ? null : new Adapter(lease);
    }
    private sealed class Adapter(Win32ReadingSelectionLease native) : IReadingSelectionLease
    {
        public ReadingSelection Selection => native.Selection;
        public int? PendingActivationKey => native.PendingActivationKey;
        public bool IsLive => native.IsLive;
        public event Action? Invalidated { add => native.Invalidated += value; remove => native.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => native.IsCurrentAsync(token);
        public Task EnrichAsync(CancellationToken token) => native.EnrichAsync(token);
        public void ArmActivationKey(int virtualKey) => native.ArmActivationKey(virtualKey);
        public void BindGesture(ReadingSelectionGesture gesture) => native.BindGesture(gesture);
        public void Retire() => native.Retire();
        public ValueTask DisposeAsync() => native.DisposeAsync();
    }
}
