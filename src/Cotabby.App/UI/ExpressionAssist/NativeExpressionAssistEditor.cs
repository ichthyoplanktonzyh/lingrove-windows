using Cotabby.Core.ExpressionAssist;
using Cotabby.Win32.ExpressionAssist;
using System.Runtime.Versioning;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Borrowed presentation adapter. The native owner retains host/clipboard/gate lifetime;
/// the UI session owns this subscription and retires it before that native owner is disposed.</summary>
[SupportedOSPlatform("windows8.1")]
internal sealed class NativeExpressionAssistEditor(Win32ExpressionEditor owner) : IExpressionAssistEditor
{
    public async Task<IExpressionAssistEditorSession?> CaptureAsync(string? trigger, nint panelHwnd,
        int? initiatingVirtualKey, Func<bool> panelOwnsKeyboard, CancellationToken cancellationToken)
    {
        var session = await owner.CaptureAsync(trigger, panelHwnd, panelOwnsKeyboard, initiatingVirtualKey, cancellationToken).ConfigureAwait(false);
        return session is null ? null : new Session(session);
    }

    private sealed class Session : IExpressionAssistEditorSession
    {
        private readonly ExpressionEditorSession _native;
        internal Session(ExpressionEditorSession native) { _native = native; native.Invalidated += OnInvalidated; }
        public ExpressionEditorReceipt Receipt => _native.Receipt;
        public event Action? Invalidated;
        private void OnInvalidated(object? sender, EventArgs args) => Invalidated?.Invoke();
        public bool EnterPanel() => _native.EnterPanel();
        public Task<bool> IsUnchangedAsync(CancellationToken cancellationToken) => _native.IsUnchangedAsync(cancellationToken);
        public bool BeginReturn() => _native.BeginReturn();
        public async Task<bool> ResumeAndReplaceAsync(string text, CancellationToken cancellationToken) =>
            await _native.ResumeAndReplaceAsync(text, cancellationToken).ConfigureAwait(false) == ExpressionEditOutcome.Verified;
        public void Retire() => _native.Retire();
        public ValueTask DisposeAsync() { _native.Invalidated -= OnInvalidated; return _native.DisposeAsync(); }
    }
}
