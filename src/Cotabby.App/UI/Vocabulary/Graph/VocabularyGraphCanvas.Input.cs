using System.Windows;
using System.Windows.Input;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Canvas input keeps the original click/drag threshold, grab offset, final-pin handshake
/// and keyboard navigation. WPF events only convert native input to local DIP/world values; tests
/// can drive the same transitions with controlled points without pretending to inject OS input.</summary>
internal sealed partial class VocabularyGraphCanvas
{
    internal VocabularyGraphNodeId? Hit(Point point)
    {
        VocabularyGraphNodeId? nearest = null; double nearestDistance = double.PositiveInfinity;
        foreach (var node in _snapshot.Nodes)
        {
            if (!_positions.TryGetValue(node.Id, out var position)) continue;
            var screen = Screen(position); double distance = (point - screen).Length;
            if (distance < 22 && distance < nearestDistance) { nearest = node.Id; nearestDistance = distance; }
        }
        return nearest;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pointer is not null && e.LeftButton == MouseButtonState.Pressed) PointerDrag(e.GetPosition(this));
        else { _hovered = Hit(e.GetPosition(this)); InvalidateVisual(); }
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _hovered = null; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonDown(e); PointerDown(e.GetPosition(this)); CaptureMouse(); e.Handled = true; }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); PointerUp(); if (IsMouseCaptured) ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // WPF may lose capture on an app switch without a mouse-up. Keep the final drag point
        // but do not turn that interruption into a click or a headword expansion.
        if (_pointer is { } pointer) { _pointer = pointer with { Moved = true }; PointerUp(); }
    }
    internal void PointerDown(Point point)
    {
        Dispatcher.VerifyAccess(); if (_retired) return;
        _presentation.FitsInitialLayout = false; Focus(); var node = Hit(point); var world = World(point);
        var position = node is { } id && _positions.TryGetValue(id, out var current) ? current : world;
        _keyboardNode = node;
        _pointer = new(node, point, _presentation.Camera.Offset, new(position.X - world.X, position.Y - world.Y));
        InvalidateVisual();
    }
    internal void PointerDrag(Point point)
    {
        Dispatcher.VerifyAccess(); if (_pointer is not { } pointer || _retired) return;
        if ((point - pointer.Start).Length > 3) pointer = pointer with { Moved = true };
        _pointer = pointer; if (!pointer.Moved) return;
        if (pointer.Node is { } node && !_snapshot.IsMeaningGraph)
        {
            _pinSerial = unchecked(_pinSerial + 1); _releasedPin = null; _pinned = node;
            var world = World(point); _pinPosition = new(world.X + pointer.GrabOffset.X, world.Y + pointer.GrabOffset.Y);
            _positions[node] = _pinPosition.Value; Wake();
        }
        else _presentation.Camera = _presentation.Camera with { Offset = new(pointer.Offset.X + point.X - pointer.Start.X, pointer.Offset.Y + point.Y - pointer.Start.Y) };
        InvalidateVisual();
    }
    internal void PointerUp()
    {
        Dispatcher.VerifyAccess(); if (_pointer is not { } pointer || _retired) return;
        _pointer = null;
        if (pointer.Moved)
        {
            if (_pinned is { } pin && _pinPosition is { } point) _releasedPin = new(pin, point, _pinSerial);
            _pinned = null; _pinPosition = null; if (!_snapshot.IsMeaningGraph) Wake();
        }
        else if (pointer.Node is { } node) Activate(node);
        else { _presentation.ClearFocus(); _select(null); InvalidateVisual(); }
    }
    internal void Activate(VocabularyGraphNodeId id)
    {
        Dispatcher.VerifyAccess(); if (_retired || !IsEnabled || !IsVisible || !_snapshot.Nodes.Any(node => node.Id == id)) return;
        _presentation.FitsInitialLayout = false;
        if (id.Kind == VocabularyGraphNodeKind.Meaning) { _presentation.Toggle(id.EntryId); _select(null); _selectMeaning(id.EntryId); }
        else if (id.Kind == VocabularyGraphNodeKind.Category) { _presentation.Toggle(id.EntryId); _select(null); _selectCategory(id.EntryId); }
        else if (id.Kind == VocabularyGraphNodeKind.Axis) _presentation.Toggle(id.EntryId);
        else if (id.Selection is { } selection) { if (!_snapshot.IsMeaningGraph) _presentation.Reveal(selection); _select(selection); }
        else { _presentation.Toggle(id.EntryId); _select(null); }
        InvalidateVisual();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); _presentation.FitsInitialLayout = false;
        // WPF exposes wheel detents, not AppKit's precise-scroll flag. Map one Windows detent
        // to three source scroll units; positive Windows delta zooms toward the pointer. Shift
        // gives horizontal pan. Native precision-touchpad pan/magnify remains a host adapter gap.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            _presentation.Camera = _presentation.Camera with { Offset = new(_presentation.Camera.Offset.X + e.Delta / 4d, _presentation.Camera.Offset.Y) };
        else Zoom(Math.Exp(e.Delta / 120d * .075), e.GetPosition(this));
        e.Handled = true; InvalidateVisual();
    }
    internal void Zoom(double factor, Point pointer)
    {
        Dispatcher.VerifyAccess(); _presentation.FitsInitialLayout = false;
        _presentation.Camera = _presentation.Camera.Zoom(factor, new(pointer.X, pointer.Y), Viewport); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); _presentation.FitsInitialLayout = false;
        if (HandleKey(e.Key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) e.Handled = true;
    }
    internal bool HandleKey(Key key, bool shift = false)
    {
        Dispatcher.VerifyAccess(); if (_retired) return false;
        _presentation.FitsInitialLayout = false;
        switch (key)
        {
            case Key.Tab: MoveKeyboard(shift); break;
            case Key.Enter: case Key.Space: if (_keyboardNode is { } id) Activate(id); break;
            case Key.Escape: _presentation.ClearFocus(); _select(null); break;
            case Key.OemPlus: case Key.Add: Execute(VocabularyGraphCommand.ZoomIn); break;
            case Key.OemMinus: case Key.Subtract: Execute(VocabularyGraphCommand.ZoomOut); break;
            case Key.Left: Pan(30, 0); break;
            case Key.Right: Pan(-30, 0); break;
            case Key.Down: Pan(0, -30); break;
            case Key.Up: Pan(0, 30); break;
            default: return false;
        }
        InvalidateVisual(); return true;
    }
    private void Pan(double x, double y) => _presentation.Camera = _presentation.Camera with { Offset = new(_presentation.Camera.Offset.X + x, _presentation.Camera.Offset.Y + y) };
    private void MoveKeyboard(bool backwards)
    {
        int? index = _keyboardNode is { } id ? _snapshot.Nodes.Select((node, i) => (node.Id, i)).Where(pair => pair.Id == id).Select(pair => (int?)pair.i).FirstOrDefault() : null;
        int next = (index ?? (backwards ? _snapshot.Nodes.Count : -1)) + (backwards ? -1 : 1);
        if (next >= 0 && next < _snapshot.Nodes.Count)
        { _keyboardNode = _snapshot.Nodes[next].Id; if (_positions.TryGetValue(_keyboardNode.Value, out var point)) Center(point); }
        else { _keyboardNode = null; MoveFocus(new TraversalRequest(backwards ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next)); }
    }
    internal void Execute(VocabularyGraphCommand command)
    {
        Dispatcher.VerifyAccess(); _presentation.FitsInitialLayout = false; var center = new VocabularyGraphPoint(ActualWidth / 2, ActualHeight / 2);
        switch (command)
        {
            case VocabularyGraphCommand.ZoomIn: _presentation.Camera = _presentation.Camera.Zoom(1.2, center, Viewport); break;
            case VocabularyGraphCommand.ZoomOut: _presentation.Camera = _presentation.Camera.Zoom(1 / 1.2, center, Viewport); break;
            case VocabularyGraphCommand.Fit: _presentation.Camera = _presentation.Camera.Fit(_positions.Values.ToArray(), Viewport); break;
            case VocabularyGraphCommand.Locate:
                if (!_canonicalLocate) { if (_presentation.FocusedEntryId is { } legacy && _positions.TryGetValue(VocabularyGraphNodeId.Headword(legacy), out var head)) Center(head); break; }
                var targets = new List<VocabularyGraphNodeId>(); if (_selection is { } selected) targets.Add(VocabularyGraphNodeId.Sense(selected)); if (_meaningSelection is { } meaning) targets.Add(VocabularyGraphNodeId.Meaning(meaning)); if (_categorySelection is { } category) targets.Add(VocabularyGraphNodeId.Category(category)); if (_presentation.FocusedEntryId is { } id) targets.AddRange(_snapshot.Nodes.Where(n => n.Id.EntryId == id).OrderBy(n => n.Id.Kind == VocabularyGraphNodeKind.Meaning ? 0 : n.Id.Kind == VocabularyGraphNodeKind.Category ? 1 : 2).Select(n => n.Id));
                foreach (var target in targets) if (_positions.TryGetValue(target, out var point)) { Center(point); break; } break;
        }
        InvalidateVisual();
    }
}
