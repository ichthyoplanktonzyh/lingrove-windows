using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Virtual button peers mirror the source's lightweight accessibility nodes. Their
/// frames are calculated lazily from the camera and current DPI; a thousand graph particles do
/// not create a thousand WPF visual controls. Old peers keep only a weak canvas reference.</summary>
internal sealed partial class VocabularyGraphCanvas
{
    private List<AutomationPeer> _automationNodes = [];
    private void RebuildAutomation()
    {
        var heads = _snapshot.Nodes.Where(node => node.Id.Kind == VocabularyGraphNodeKind.Entry && node.Id.Selection is null).ToDictionary(node => node.Id.EntryId, node => node.Label);
        _automationNodes = _snapshot.Nodes.Select(node => (AutomationPeer)new GraphNodePeer(this, node,
            node.Id.Selection is null || !heads.TryGetValue(node.Id.EntryId, out var head) ? node.Label : head + " · Core meaning: " + node.Label)).ToList();
        if (UIElementAutomationPeer.FromElement(this) is { } peer) { peer.ResetChildrenCache(); peer.InvalidatePeer(); }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new GraphCanvasPeer(this);
    private Rect NodeAutomationFrame(VocabularyGraphNodeId id)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || _window is null || !_positions.TryGetValue(id, out var position) || PresentationSource.FromVisual(this) is null) return Rect.Empty;
        var point = Screen(position); var a = PointToScreen(new(point.X - 22, point.Y - 22)); var b = PointToScreen(new(point.X + 22, point.Y + 22));
        return new(a, b);
    }
    private sealed class GraphCanvasPeer(VocabularyGraphCanvas canvas) : FrameworkElementAutomationPeer(canvas)
    {
        protected override string GetNameCore() => "Vocabulary graph";
        protected override string GetAutomationIdCore() => "vocabularyGraph";
        protected override string GetClassNameCore() => nameof(VocabularyGraphCanvas);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override List<AutomationPeer> GetChildrenCore() => canvas._automationNodes;
    }
    private sealed class GraphNodePeer : AutomationPeer, IInvokeProvider
    {
        private readonly WeakReference<VocabularyGraphCanvas> _canvas;
        private readonly VocabularyGraphNodeId _id;
        private readonly string _label;
        internal GraphNodePeer(VocabularyGraphCanvas canvas, VocabularyGraphNode node, string label)
        { _canvas = new(canvas); _id = node.Id; _label = label; }
        protected override string GetNameCore() => _label;
        protected override string GetAutomationIdCore() => (_id.Kind == VocabularyGraphNodeKind.Entry ? "" : _id.Kind + "/") + _id.EntryId.ToString("D") + (_id.SenseId is { } sense ? "/" + sense.ToString("D") : "");
        protected override string GetClassNameCore() => "VocabularyGraphNode";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override Rect GetBoundingRectangleCore() => _canvas.TryGetTarget(out var canvas) ? canvas.NodeAutomationFrame(_id) : Rect.Empty;
        protected override bool IsOffscreenCore()
        {
            var bounds = GetBoundingRectangleCore();
            if (bounds.IsEmpty || !_canvas.TryGetTarget(out var canvas)) return true;
            return !bounds.IntersectsWith(new Rect(canvas.PointToScreen(new(0, 0)), canvas.PointToScreen(new(canvas.ActualWidth, canvas.ActualHeight))));
        }
        protected override bool IsKeyboardFocusableCore() => true;
        protected override string GetAcceleratorKeyCore() => "";
        protected override string GetAccessKeyCore() => "";
        protected override string GetHelpTextCore() => "";
        protected override string GetItemStatusCore() => "";
        protected override string GetItemTypeCore() => "";
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override bool HasKeyboardFocusCore() => _canvas.TryGetTarget(out var canvas) && canvas.IsKeyboardFocused && canvas._keyboardNode == _id;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => _canvas.TryGetTarget(out var canvas) && !canvas._retired && canvas.IsEnabled && canvas.IsVisible && canvas._snapshot.Nodes.Any(n => n.Id == _id);
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
        protected override Point GetClickablePointCore()
        { var bounds = GetBoundingRectangleCore(); return bounds.IsEmpty ? new(double.NaN, double.NaN) : new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2); }
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        // WPF's ElementProxy/ElementUtil bridge marshals peer property/focus requests to this
        // peer's captured dispatcher. Direct peer APIs have the same dispatcher-only contract;
        // raw pattern invocation additionally supports callers which bypass that framework bridge.
        protected override void SetFocusCore()
        {
            Dispatcher.VerifyAccess();
            if (!_canvas.TryGetTarget(out var canvas) || canvas._retired || !canvas.IsEnabled || !canvas.IsVisible || !canvas._snapshot.Nodes.Any(n => n.Id == _id)) return;
            canvas._keyboardNode = _id; canvas.Focus(); canvas.InvalidateVisual();
        }
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : null;
        void IInvokeProvider.Invoke()
        {
            if (!_canvas.TryGetTarget(out var canvas) || canvas.Dispatcher.HasShutdownStarted) throw new ElementNotAvailableException();
            if (canvas.Dispatcher.CheckAccess())
            {
                if (canvas._retired || !canvas._snapshot.Nodes.Any(n => n.Id == _id)) throw new ElementNotAvailableException();
                if (!canvas.IsEnabled || !canvas.IsVisible) throw new ElementNotEnabledException();
                canvas.Activate(_id);
            }
            else canvas.Dispatcher.BeginInvoke(() =>
            {
                // A queued accessibility request can outlive a topology change or retirement.
                // Recheck on the UI thread and decline silently rather than throwing on WPF's loop.
                if (!canvas._retired && canvas.IsEnabled && canvas.IsVisible && canvas._snapshot.Nodes.Any(n => n.Id == _id)) canvas.Activate(_id);
            });
        }
    }
}
