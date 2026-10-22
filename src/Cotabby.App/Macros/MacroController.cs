using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.Macros;
using Cotabby.Core.Overlay;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Macros;

/// <summary>
/// Drives inline slash macros. Typing <c>/5+5</c> previews the result at the caret
/// and the accept key replaces the typed macro with it.
/// </summary>
public sealed class MacroController : IDisposable
{
    private readonly IKeyboardHook _hook;
    private readonly IFocusTracker _focus;
    private readonly ITextInserter _inserter;
    private readonly IOverlayPresenter _preview;
    private readonly ILogger<MacroController> _logger;
    private readonly MacroEngine _engine = MacroEngine.Standard();
    private readonly MacroTriggerStateMachine _machine = new();
    private readonly object _gate = new();
    private MacroResult? _result;
    private bool _enabled = true;
    private FocusedField? _captureField;

    private const int CaretSettleMs = 30;

    public MacroController(
        IKeyboardHook hook, IFocusTracker focus, ITextInserter inserter, IOverlayPresenter preview,
        ILogger<MacroController> logger)
    {
        _hook = hook;
        _focus = focus;
        _inserter = inserter;
        _preview = preview;
        _logger = logger;
        _hook.KeyEvent += OnKeyEvent;
        _focus.FocusChanged += OnFocusChanged;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Cancel();
        }
    }

    /// <summary>The key that inserts the result in an app. It follows the accept-word shortcut.</summary>
    public Func<string?, KeyBinding> CommitKeyFor { get; set; } = _ => KeyBinding.Tab;

    /// <summary>Whether a macro may open in a field. The host supplies the app-wide rules.</summary>
    public Func<FocusedField, bool> CanCapture { get; set; } = _ => true;

    /// <summary>Whether a macro is being typed, during which suggestions stand down.</summary>
    public bool IsCapturing
    {
        get { lock (_gate) return _machine.IsActive; }
    }

    public void Dispose()
    {
        _hook.KeyEvent -= OnKeyEvent;
        _focus.FocusChanged -= OnFocusChanged;
        Cancel();
    }

    private void OnKeyEvent(object? sender, KeyEventArgs args)
    {
        if (!_enabled) return;
        lock (_gate)
        {
            if (args.Suppress)
            {
                End();
                return;
            }

            var outcome = _machine.Apply(InlineCommitKey.Normalize(args.Event, CommitKeyFor(_focus.Current?.ProcessName)), _result is not null);
            args.Suppress = _machine.ConsumesKey;
            switch (outcome)
            {
                case MacroTriggerStateMachine.Outcome.Opened:
                    _captureField = _focus.Current;
                    if (!InsertionTargetPolicy.IsEligibleField(_captureField) || !CanCapture(_captureField!)) End();
                    break;
                case MacroTriggerStateMachine.Outcome.QueryChanged:
                    Preview(_engine.Evaluate(_machine.Query));
                    break;
                case MacroTriggerStateMachine.Outcome.Commit when _result is { } result:
                    var captured = _captureField;
                    string expectedRun = "/" + _machine.LastCommittedQuery;
                    Preview(null);
                    _captureField = null;
                    if (captured is not null) _ = Task.Run(() => CommitAsync(captured, expectedRun, result));
                    break;
                case MacroTriggerStateMachine.Outcome.Cancel:
                    // The state machine retains the invalidating character for the next trigger's
                    // boundary check. Resetting it here would make an in-word slash look eligible.
                    _captureField = null;
                    Preview(null);
                    break;
            }
        }
    }

    private void Preview(MacroResult? result)
    {
        _result = result;
        if (result is null)
        {
            _preview.Hide();
            return;
        }
        // The caret is read off the hook thread, after the host has applied the key.
        _ = Task.Run(async () =>
        {
            await Task.Delay(CaretSettleMs);
            var field = _focus.Refresh();
            lock (_gate)
            {
                if (ReferenceEquals(_result, result) && InsertionTargetPolicy.IsSameField(_captureField, field))
                {
                    _preview.Show(result.PreviewText, field!.CaretRect, OverlayStyle.Popup);
                }
            }
        });
    }

    private async Task CommitAsync(FocusedField capture, string expectedRun, MacroResult result)
    {
        try
        {
            var field = InlineQueryReplacementPolicy.Resolve(capture, expectedRun, _focus.Refresh());
            if (field is null) return;
            if (await _inserter.ReplaceAsync(field, expectedRun.Length, result.InsertionText, CancellationToken.None))
            {
                _logger.LogInformation("Macro committed (replaced {Count} chars).", expectedRun.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Macro commit failed.");
        }
    }

    private void OnFocusChanged(object? sender, FocusedField? field)
    {
        lock (_gate)
        {
            if (_machine.IsActive && !InsertionTargetPolicy.IsSameField(_captureField, field)) End();
        }
    }

    private void Cancel()
    {
        lock (_gate) End();
    }

    private void End()
    {
        _machine.Reset();
        _captureField = null;
        Preview(null);
    }
}
