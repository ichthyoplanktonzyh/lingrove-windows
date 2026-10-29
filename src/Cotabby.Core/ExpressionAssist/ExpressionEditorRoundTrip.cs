namespace Cotabby.Core.ExpressionAssist;

/// <summary>
/// One request's explicit host-to-owned-panel-to-host authorization. The caller supplies only
/// observations obtained by its existing focus owner, and invalidates immediately on any other
/// focus, panel closure, or superseding request. This does not relax general focus-epoch rules.
/// </summary>
public sealed class ExpressionEditorRoundTrip
{
    private readonly object _gate = new();
    private readonly ExpressionEditorReceipt _saved;
    private readonly nint _hostWindow;
    private readonly nint _panelWindow;
    private readonly int _ownerProcess;
    private Phase _phase = Phase.Captured;
    private ExpressionEditorObservation? _firstReturn;
    private ExpressionEditorObservation? _confirmed;

    private enum Phase { Captured, Panel, Returning, Confirmed, Consumed, Retired }

    public ExpressionEditorRoundTrip(ExpressionEditorReceipt saved, nint hostWindow, nint panelWindow, int ownerProcess)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!ExpressionEditorReceiptPolicy.MatchesOriginal(saved, saved.Original) || hostWindow == 0 || panelWindow == 0
            || hostWindow == panelWindow || ownerProcess <= 0 || ownerProcess == saved.Original.ProcessId)
            throw new ArgumentException("A valid host receipt and distinct owned panel are required.");
        _saved = saved;
        _hostWindow = hostWindow;
        _panelWindow = panelWindow;
        _ownerProcess = ownerProcess;
    }

    /// <summary>The platform has registered this exact panel HWND and verified its keyboard ownership.</summary>
    public bool EnterPanel(nint foregroundWindow, int foregroundProcess, bool registeredPanelOwnsKeyboard)
    {
        lock (_gate)
        {
            if (_phase != Phase.Captured || foregroundWindow != _panelWindow || foregroundProcess != _ownerProcess || !registeredPanelOwnsKeyboard)
            { _phase = Phase.Retired; return false; }
            _phase = Phase.Panel;
            return true;
        }
    }

    /// <summary>Check each focus event while the panel is active; a third window retires the session.</summary>
    public bool ObservePanelFocus(nint foregroundWindow, int foregroundProcess, bool registeredPanelOwnsKeyboard)
    {
        lock (_gate)
        {
            if (_phase == Phase.Panel && foregroundWindow == _panelWindow && foregroundProcess == _ownerProcess && registeredPanelOwnsKeyboard) return true;
            _phase = Phase.Retired;
            return false;
        }
    }

    /// <summary>Only the registered panel may explicitly request returning to the original HWND.</summary>
    public bool BeginReturn(nint foregroundWindow, int foregroundProcess, bool registeredPanelOwnsKeyboard)
    {
        lock (_gate)
        {
            if (_phase != Phase.Panel || foregroundWindow != _panelWindow || foregroundProcess != _ownerProcess || !registeredPanelOwnsKeyboard)
            { _phase = Phase.Retired; return false; }
            _phase = Phase.Returning;
            return true;
        }
    }

    /// <summary>Two independent exact reads must settle in the same newly focused host epoch.</summary>
    public bool ObserveReturnedHost(nint foregroundWindow, ExpressionEditorObservation? observation)
    {
        lock (_gate)
        {
            if (_phase != Phase.Returning || foregroundWindow != _hostWindow || !ExpressionEditorReceiptPolicy.MatchesOriginal(_saved, observation)
                || observation!.FocusEpoch == _saved.Original.FocusEpoch)
            { _phase = Phase.Retired; return false; }
            if (_firstReturn is null) { _firstReturn = observation; return false; }
            if (_firstReturn.FocusEpoch != observation.FocusEpoch || _firstReturn.CompositionSequence != observation.CompositionSequence
                || observation.ObservationVersion <= _firstReturn.ObservationVersion)
            { _phase = Phase.Retired; return false; }
            _confirmed = observation;
            _phase = Phase.Confirmed;
            return true;
        }
    }

    /// <summary>Claim before any native write; failure or uncertainty never restores the claim.</summary>
    public ExpressionEditorObservation? Consume(nint foregroundWindow, ExpressionEditorObservation? latest)
    {
        lock (_gate)
        {
            if (_phase != Phase.Confirmed || foregroundWindow != _hostWindow || !ExpressionEditorReceiptPolicy.MatchesOriginal(_saved, latest)
                || latest!.FocusEpoch != _confirmed!.FocusEpoch || latest.CompositionSequence != _confirmed.CompositionSequence
                || latest.ObservationVersion <= _confirmed.ObservationVersion)
            { _phase = Phase.Retired; return null; }
            _phase = Phase.Consumed;
            return latest;
        }
    }

    public void Retire() { lock (_gate) _phase = Phase.Retired; }
}
