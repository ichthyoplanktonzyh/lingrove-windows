namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Unsupported Windows versions cannot supply retained TextEdit composition evidence.</summary>
internal sealed class UnavailableExpressionAssistEditor : IExpressionAssistEditor
{
    public Task<IExpressionAssistEditorSession?> CaptureAsync(string? trigger, nint panelHwnd,
        int? initiatingVirtualKey, Func<bool> panelOwnsKeyboard, CancellationToken cancellationToken) =>
        Task.FromResult<IExpressionAssistEditorSession?>(null);
}
