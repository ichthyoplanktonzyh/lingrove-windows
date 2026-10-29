using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Window-local drawing surface adapted from Mac GraphCanvasView. One animation task
/// asks the isolated worker for copied frames and stops on cooling/hide. UI state stays on this
/// dispatcher; old replies cannot overwrite a newer epoch, pointer or final released pin.</summary>
internal sealed partial class VocabularyGraphCanvas : FrameworkElement
{
    private readonly VocabularyGraphPresentation _presentation;
    private readonly Action<Task>? _track;
    private VocabularyGraphSnapshot _snapshot = new();
    private VocabularySelection? _selection;
    private Action<VocabularySelection?> _select = _ => { };
    private Action<Guid> _selectMeaning = _ => { }, _selectCategory = _ => { };
    private Guid? _meaningSelection, _categorySelection;
    private bool _canonicalLocate;
    private Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> _positions = [];
    private VocabularyGraphSettings _applied = new();
    private CancellationTokenSource? _animationCancellation;
    private Task? _animation;
    private readonly HashSet<Task> _work = [];
    private ulong _epoch, _pinSerial;
    private long _commandSerial;
    private bool _needsLayoutWake = true, _retired;
    private Window? _window;
    private VocabularyGraphNodeId? _hovered, _pinned, _keyboardNode, _pendingSearchTarget;
    private VocabularyGraphPoint? _pinPosition;
    private ReleasedPin? _releasedPin;
    private PointerSession? _pointer;
    private sealed record ReleasedPin(VocabularyGraphNodeId Node, VocabularyGraphPoint Position, ulong Serial);
    private sealed record FrameInput(VocabularyGraphNodeId? Node, VocabularyGraphPoint? Position, ulong? ReleaseSerial);
    private sealed record PointerSession(VocabularyGraphNodeId? Node, Point Start, VocabularyGraphPoint Offset, VocabularyGraphPoint GrabOffset, bool Moved = false);
    internal int RenderedFrameCount { get; private set; }
    internal bool IsAnimating => _animation is not null;
    internal Task LastAnimation { get; private set; } = Task.CompletedTask;
    internal Task LastSuspension { get; private set; } = Task.CompletedTask;
    internal IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> Positions => _positions.ToDictionary();
    internal VocabularyGraphNodeId? KeyboardNode => _keyboardNode;

    internal VocabularyGraphCanvas(VocabularyGraphPresentation presentation, Action<Task>? track = null)
    {
        _presentation = presentation; _track = track; Focusable = true; ClipToBounds = true;
        Loaded += (_, _) => { AttachWindow(); Wake(); };
        Unloaded += (_, _) => { if (!_retired) Suspend(); DetachWindow(); };
        IsVisibleChanged += (_, _) => { if (!_retired) { if (IsVisible) Wake(); else Suspend(); } };
        SizeChanged += (_, _) =>
        {
            if (!_presentation.HasCamera && ActualWidth > 0 && _positions.Count != 0)
            { _presentation.Camera = _presentation.Camera.Fit(_positions.Values.ToArray(), Viewport); _presentation.HasCamera = true; }
            InvalidateVisual();
        };
    }
    internal void Update(VocabularyGraphSnapshot snapshot, VocabularySelection? selection, Action<VocabularySelection?> select, Guid? meaningSelection = null, Guid? categorySelection = null, Action<Guid>? selectMeaning = null, Action<Guid>? selectCategory = null, bool canonicalLocate = false)
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _select = select; _canonicalLocate = canonicalLocate; if (_categorySelection != categorySelection && categorySelection is { } category) _pendingSearchTarget = VocabularyGraphNodeId.Category(category); _categorySelection = categorySelection; if (_meaningSelection != meaningSelection && meaningSelection is { } meaning) _pendingSearchTarget = VocabularyGraphNodeId.Meaning(meaning); _meaningSelection = meaningSelection; _selectMeaning = selectMeaning ?? (_ => { }); _selectCategory = selectCategory ?? (_ => { });
        if (_selection != selection && selection is { } target) _pendingSearchTarget = VocabularyGraphNodeId.Sense(target);
        _selection = selection; var oldMatches = _snapshot.Matches; string oldQuery = _snapshot.SearchQuery;
        bool changed = _snapshot != snapshot; _snapshot = snapshot;
        if (_pinned is { } pin && !snapshot.Nodes.Any(n => n.Id == pin)) { _pinned = null; _pinPosition = null; _releasedPin = null; _pointer = null; }
        if (_keyboardNode is { } keyboard && !snapshot.Nodes.Any(n => n.Id == keyboard)) _keyboardNode = null;
        var settings = _presentation.Settings.Normalized;
        bool forceChanged = _applied.Center != settings.Center || _applied.Repulsion != settings.Repulsion
            || _applied.LinkStrength != settings.LinkStrength || _applied.LinkDistance != settings.LinkDistance;
        _applied = settings;
        if (changed) { _needsLayoutWake = true; RefreshLabelDirections(); RebuildAutomation(); }
        if (changed || forceChanged) { _needsLayoutWake = true; Wake(); }
        if (_commandSerial != _presentation.CommandSerial) { _commandSerial = _presentation.CommandSerial; Execute(_presentation.Command); }
        if (!oldMatches.SetEquals(snapshot.Matches) || oldQuery != snapshot.SearchQuery)
        { _pendingSearchTarget = snapshot.Nodes.FirstOrDefault(n => snapshot.Matches.Contains(n.Id))?.Id; CenterSearchResult(); }
        InvalidateVisual();
    }
    private VocabularyGraphSize Viewport => new(ActualWidth, ActualHeight);
    private bool CanAnimate => !_retired && IsLoaded && IsVisible && _window is { IsVisible: true, WindowState: not WindowState.Minimized };
    private void AttachWindow()
    {
        var window = Window.GetWindow(this); if (ReferenceEquals(window, _window)) return;
        DetachWindow(); _window = window; if (window is null) return;
        window.StateChanged += OnWindowState; window.Closed += OnWindowClosed;
    }
    private void DetachWindow()
    { if (_window is { } window) { window.StateChanged -= OnWindowState; window.Closed -= OnWindowClosed; } _window = null; }
    private void OnWindowState(object? sender, EventArgs e) { if (!_retired) { if (CanAnimate) Wake(); else Suspend(); } }
    private void OnWindowClosed(object? sender, EventArgs e) { if (!_retired) Suspend(); }
    internal void Suspend()
    {
        Dispatcher.VerifyAccess();
        CancelAnimation(); _animationCancellation = null; _animation = null;
        _epoch = _presentation.NextEpoch();
        var node = _pinned ?? _releasedPin?.Node; var position = _pinPosition ?? _releasedPin?.Position;
        _pinned = null; _pinPosition = null; _pointer = null; _releasedPin = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        _needsLayoutWake = true;
        LastSuspension = _presentation.Worker.SuspendAsync(_epoch, node, position); Track(LastSuspension);
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (!_retired) { _retired = true; Suspend(); DetachWindow(); }
        return DrainAsync();
    }
    internal Task DrainAsync() { lock (_work) return Task.WhenAll(_work.ToArray()); }
    private void Track(Task task)
    {
        lock (_work) _work.Add(task); _track?.Invoke(task);
        _ = task.ContinueWith(done => { lock (_work) _work.Remove(done); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Wake()
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        if (!CanAnimate || _snapshot.Nodes.Count == 0)
        { if (_snapshot.Nodes.Count == 0) { Suspend(); _positions = []; InvalidateVisual(); } return; }
        if (_snapshot.IsMeaningGraph && _snapshot.TreePositions.Count > 0)
        {
            CancelAnimation(); _animation = null; _animationCancellation = null; _needsLayoutWake = false; _epoch = _presentation.NextEpoch();
            Apply(new VocabularyGraphFrame { Positions = _snapshot.TreePositions, IsRunning = false }, _epoch); return;
        }
        if (_animation is not null) { if (!_needsLayoutWake) return; CancelAnimation(); }
        _needsLayoutWake = false; _epoch = _presentation.NextEpoch();
        ulong ticket = _epoch; var worker = _presentation.Worker; var graph = _snapshot; var settings = _applied;
        var cancellation = new CancellationTokenSource(); _animationCancellation = cancellation;
        // The task is tracked before this dispatcher returns. Neither worker waits nor delay/
        // cancellation cleanup require a dispatcher continuation to complete at application exit.
        LastAnimation = _animation = Task.Run(async () =>
        {
            var token = cancellation.Token;
            try
            {
                var frame = await worker.ConfigureAsync(graph, settings, ticket).ConfigureAwait(false);
                if (frame is null || token.IsCancellationRequested) return;
                await Ui(() => Apply(frame, ticket)).ConfigureAwait(false); bool running = frame.IsRunning;
                while (!token.IsCancellationRequested)
                {
                    var input = await Ui(() =>
                    {
                        var value = Input(ticket);
                        // Decide to stop AND publish idle in one dispatcher turn. A drag that
                        // arrives afterwards must be able to start a new loop immediately;
                        // optional Background cleanup cannot own that handoff.
                        if ((value is null || (!running && value.Node is null)) && _epoch == ticket)
                        { _animation = null; _animationCancellation = null; }
                        return value;
                    }).ConfigureAwait(false);
                    if (input is null || (!running && input.Node is null)) break;
                    var next = await worker.AdvanceAsync(ticket, input.Node, input.Position).ConfigureAwait(false);
                    if (next is null || token.IsCancellationRequested) break;
                    await Ui(() => { if (_epoch == ticket && !token.IsCancellationRequested) { Apply(next, ticket); AcknowledgeRelease(input); } }).ConfigureAwait(false);
                    running = next.IsRunning; await Task.Delay(16, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
            finally
            {
                // Posting final UI bookkeeping is optional. Joining this task after dispatcher
                // shutdown must never wait for a callback which can no longer be delivered.
                try
                {
                    if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(() => { if (_epoch == ticket) { _animation = null; _animationCancellation = null; } }, DispatcherPriority.Background);
                }
                catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
                cancellation.Dispose();
            }
        });
        Track(LastAnimation);
    }
    private void CancelAnimation()
    {
        // The worker can finish/dispose its CTS before optional UI bookkeeping arrives.
        try { _animationCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }
    private async Task Ui(Action action)
    { if (Dispatcher.HasShutdownStarted) return; await Dispatcher.InvokeAsync(action, DispatcherPriority.Render).Task.ConfigureAwait(false); }
    private async Task<T?> Ui<T>(Func<T?> action) where T : class
    { if (Dispatcher.HasShutdownStarted) return null; return await Dispatcher.InvokeAsync(action, DispatcherPriority.Render).Task.ConfigureAwait(false); }
    private FrameInput? Input(ulong epoch)
    {
        if (_epoch != epoch || !CanAnimate) return null;
        if (_pinned is { } pin) return new(pin, _pinPosition, null);
        if (_releasedPin is { } release) return new(release.Node, release.Position, release.Serial);
        return new(null, null, null);
    }
    private void Apply(VocabularyGraphFrame frame, ulong epoch)
    {
        if (_epoch != epoch || _retired) return;
        _positions = frame.Positions.ToDictionary();
        if (_pinned is { } pin && _pinPosition is { } point) _positions[pin] = point;
        else if (_releasedPin is { } release) _positions[release.Node] = release.Position;
        RenderedFrameCount++;
        if (_presentation.FitsInitialLayout && ActualWidth > 0 && ActualHeight > 0 && _positions.Count != 0)
        {
            _presentation.Camera = _presentation.Camera.Fit(_positions.Values.ToArray(), Viewport); _presentation.HasCamera = true;
            if (!frame.IsRunning) _presentation.FitsInitialLayout = false;
        }
        CenterSearchResult(); InvalidateVisual();
    }
    private void AcknowledgeRelease(FrameInput input)
    { if (input.ReleaseSerial is { } serial && _releasedPin?.Serial == serial) _releasedPin = null; }
    private void CenterSearchResult()
    {
        if (_pendingSearchTarget is { } target && _positions.TryGetValue(target, out var point))
        { _presentation.FitsInitialLayout = false; Center(point); _pendingSearchTarget = null; }
    }
    private void Center(VocabularyGraphPoint point) => _presentation.Camera = _presentation.Camera with { Offset = new(-point.X * _presentation.Camera.Scale, -point.Y * _presentation.Camera.Scale) };
    internal Point Screen(VocabularyGraphPoint point) { var p = _presentation.Camera.Screen(point, Viewport); return new(p.X, p.Y); }
    private VocabularyGraphPoint World(Point point) => _presentation.Camera.World(new(point.X, point.Y), Viewport);
}
