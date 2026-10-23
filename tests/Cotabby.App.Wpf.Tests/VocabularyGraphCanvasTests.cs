using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Text;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real compiled WPF canvas, original graph worker and controlled DIP input transitions.
/// These tests draw to a memory bitmap and query actual peers; they inject no OS pointer/keyboard
/// input and claim no desktop/UIA-client, precision-touchpad or compositor-occlusion validation.</summary>
[Collection("WPF views")]
public sealed class VocabularyGraphCanvasTests(WpfDispatcher dispatcher)
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static VocabularyGraphNodeId Head(int n) => VocabularyGraphNodeId.Headword(Id(n));
    private static readonly VocabularySelection Sense = new(Id(1), Id(2));
    private static VocabularyGraphSnapshot Graph(bool sense = false) => new()
    {
        Nodes = sense ? [new(Head(1), "bank"), new(VocabularyGraphNodeId.Sense(Sense), "河岸")] : [new(Head(1), "bank"), new(Head(3), "café")],
        Edges = sense ? [new(Head(1), VocabularyGraphNodeId.Sense(Sense))] : [], HeadwordCount = sense ? 1 : 2
    };
    private sealed class Preferences : IVocabularyGraphPreferences
    {
        private byte[]? _data;
        public byte[]? ReadSettings() => _data?.ToArray();
        public void WriteSettings(ReadOnlySpan<byte> data) => _data = data.ToArray();
        public string BrowserMode { get; set; } = "list";
    }
    private sealed class View : IAsyncDisposable
    {
        public VocabularyGraphPresentation Presentation { get; } = new(new Preferences());
        public VocabularyGraphCanvas Canvas { get; }
        public Window Window { get; }
        public List<VocabularySelection?> Selections { get; } = [];
        public View(VocabularyGraphSnapshot graph, bool show = true)
        {
            Canvas = new(Presentation) { Width = 620, Height = 400 };
            Window = new() { Content = Canvas, Width = 660, Height = 460, Title = "Graph component validation" };
            Canvas.Update(graph, null, selection => Selections.Add(selection));
            if (show) Window.Show();
        }
        public async ValueTask DisposeAsync()
        {
            try { await Canvas.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { Window.Content = null; Window.Close(); }
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { Assert.True(DateTime.UtcNow < limit, "Expected graph UI state did not arrive."); await Task.Delay(5); }
    }
    private static async Task Cooled(View view)
    { await Until(() => view.Canvas.RenderedFrameCount != 0); await view.Canvas.LastAnimation.WaitAsync(TimeSpan.FromSeconds(10)); await Until(() => !view.Canvas.IsAnimating); }
    [Fact]
    public Task OriginalWorkerFramesCoolAndActualDrawingPaintsTheProjectedDots() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Cooled(view);
        var canvas = view.Canvas; Assert.Equal(2, canvas.Positions.Count); Assert.All(canvas.Positions.Values, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
        int frames = canvas.RenderedFrameCount; await Task.Delay(60); Assert.Equal(frames, canvas.RenderedFrameCount);
        Assert.True(view.Presentation.HasCamera); Assert.False(view.Presentation.FitsInitialLayout);
        await Dispatcher.Yield(DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(620, 400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        byte[] pixels = new byte[620 * 400 * 4]; bitmap.CopyPixels(pixels, 620 * 4, 0);
        var dot = canvas.Screen(canvas.Positions[Head(1)]); int x = (int)Math.Round(dot.X), y = (int)Math.Round(dot.Y);
        Assert.InRange(x, 2, 617); Assert.InRange(y, 2, 397);
        Assert.False(pixels.AsSpan((y * 620 + x) * 4, 4).SequenceEqual(pixels.AsSpan(0, 4)));
    });
    [Fact]
    public Task ClickingAHeadwordExpandsAndClickingAnExactSenseSelectsItsStableIdentity() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph(sense: true)); await Cooled(view); var canvas = view.Canvas;
        var head = canvas.Screen(canvas.Positions[Head(1)]); canvas.PointerDown(head); canvas.PointerUp();
        Assert.Contains(Id(1), view.Presentation.Expanded); Assert.Null(view.Selections.Single());
        var sense = canvas.Screen(canvas.Positions[VocabularyGraphNodeId.Sense(Sense)]); canvas.PointerDown(sense); canvas.PointerUp();
        Assert.Equal(Sense, view.Selections.Last()); Assert.Equal(Id(1), view.Presentation.FocusedEntryId);
    });
    [Fact]
    public Task AShortDragAfterCoolingKeepsGrabOffsetAndSurvivesImmediateHideWithoutAClick() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph(sense: true)); await Cooled(view); var canvas = view.Canvas; var presentation = view.Presentation;
        var original = canvas.Positions[Head(1)]; var center = canvas.Screen(original); double scale = presentation.Camera.Scale;
        canvas.PointerDown(new(center.X + 2, center.Y - 1)); canvas.PointerDrag(new(center.X + 82, center.Y + 39)); canvas.PointerUp();
        var expected = new VocabularyGraphPoint(original.X + 80 / scale, original.Y + 40 / scale);
        AssertPoint(expected, canvas.Positions[Head(1)]); Assert.Empty(view.Selections);
        view.Window.Hide(); await canvas.LastSuspension.WaitAsync(TimeSpan.FromSeconds(10));
        var restored = await presentation.Worker.ConfigureAsync(Graph(sense: true), presentation.Settings, presentation.NextEpoch()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(restored); AssertPoint(expected, restored.Positions[Head(1)]); Assert.False(canvas.IsAnimating);
    });
    [Fact]
    public Task RepliesWhileDraggingCannotOverwriteTheImmediatePointerPosition() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph(sense: true)); await Until(() => view.Canvas.Positions.Count == 2);
        var canvas = view.Canvas; var original = canvas.Positions[Head(1)]; var center = canvas.Screen(original); double scale = view.Presentation.Camera.Scale;
        canvas.PointerDown(center); canvas.PointerDrag(new(center.X + 120, center.Y + 50)); int before = canvas.RenderedFrameCount;
        var expected = new VocabularyGraphPoint(original.X + 120 / scale, original.Y + 50 / scale);
        await Until(() => canvas.RenderedFrameCount >= before + 3); AssertPoint(expected, canvas.Positions[Head(1)]); Assert.Empty(view.Selections);
        canvas.PointerUp();
    });
    [Fact]
    public Task AClickInsideTheThresholdDoesNotMoveTheNodeAndBackgroundDragPansWithoutHeating() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Cooled(view); var canvas = view.Canvas; var p = view.Presentation;
        var point = canvas.Screen(canvas.Positions[Head(1)]); canvas.PointerDown(point); canvas.PointerDrag(new(point.X + 2, point.Y)); canvas.PointerUp();
        Assert.Contains(Id(1), p.Expanded); int frames = canvas.RenderedFrameCount; var old = p.Camera.Offset;
        Assert.Null(canvas.Hit(new(8, 8))); canvas.PointerDown(new(8, 8)); canvas.PointerDrag(new(28, 38)); canvas.PointerUp();
        Assert.Equal(new VocabularyGraphPoint(old.X + 20, old.Y + 30), p.Camera.Offset); Assert.False(canvas.IsAnimating); Assert.Equal(frames, canvas.RenderedFrameCount);
    });
    [Fact]
    public Task CameraAndKeyboardCommandsNavigateWithoutChangingParticleCoordinates() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph(sense: true)); await Cooled(view); var canvas = view.Canvas; var p = view.Presentation;
        var frozen = canvas.Positions; var pointer = new Point(123, 233); var size = new VocabularyGraphSize(canvas.ActualWidth, canvas.ActualHeight);
        var anchor = p.Camera.World(new(pointer.X, pointer.Y), size); canvas.Zoom(1.7, pointer); AssertPoint(anchor, p.Camera.World(new(pointer.X, pointer.Y), size));
        Assert.True(canvas.HandleKey(Key.Tab)); Assert.Equal(Head(1), canvas.KeyboardNode);
        canvas.HandleKey(Key.Tab); Assert.Equal(VocabularyGraphNodeId.Sense(Sense), canvas.KeyboardNode); canvas.HandleKey(Key.Enter); Assert.Equal(Sense, view.Selections.Last());
        canvas.HandleKey(Key.Escape); Assert.Null(view.Selections.Last()); Assert.Null(p.FocusedEntryId);
        var offset = p.Camera.Offset; canvas.HandleKey(Key.Left); Assert.Equal(offset.X + 30, p.Camera.Offset.X); canvas.HandleKey(Key.Right); Assert.Equal(offset, p.Camera.Offset);
        canvas.Execute(VocabularyGraphCommand.Fit); Assert.Equal(frozen.OrderBy(p => p.Key.SenseId), canvas.Positions.OrderBy(p => p.Key.SenseId)); Assert.False(canvas.IsAnimating);
    });
    [Fact]
    public Task SearchCentersTheOriginalInitialMatchAndPreventsSubsequentSettlingFit() => dispatcher.RunAsync(async () =>
    {
        var graph = Graph() with { Matches = new HashSet<VocabularyGraphNodeId> { Head(1) }, SearchQuery = "bank" };
        await using var view = new View(graph); await Until(() => view.Canvas.RenderedFrameCount >= 3);
        Assert.False(view.Presentation.FitsInitialLayout); AssertPoint(new(-60 * view.Presentation.Camera.Scale, 0), view.Presentation.Camera.Offset);
    });
    [Fact]
    public Task HideStopsFramesAndReopenKeepsCoordinatesWhileAnEmptyUpdateCannotRestoreOldNodes() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Cooled(view); var canvas = view.Canvas;
        view.Window.Hide(); await canvas.LastSuspension.WaitAsync(TimeSpan.FromSeconds(10)); await canvas.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        int frames = canvas.RenderedFrameCount; var positions = canvas.Positions; await Task.Delay(40); Assert.Equal(frames, canvas.RenderedFrameCount); Assert.False(canvas.IsAnimating);
        var cached = await view.Presentation.Worker.ConfigureAsync(Graph(), view.Presentation.Settings, view.Presentation.NextEpoch()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(cached); foreach (var pair in positions) AssertPoint(pair.Value, cached.Positions[pair.Key]);
        view.Window.Show(); await Until(() => canvas.RenderedFrameCount > frames);
        Assert.Equal(positions.Keys.OrderBy(id => id.EntryId).ThenBy(id => id.SenseId), canvas.Positions.Keys.OrderBy(id => id.EntryId).ThenBy(id => id.SenseId));
        canvas.Update(new(), null, _ => { }); await canvas.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Empty(canvas.Positions); Assert.False(canvas.IsAnimating);
    });
    [Fact]
    public Task MinimizeSuspendsAndRestoringStartsTheRetainedGraphAgain() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Until(() => view.Canvas.RenderedFrameCount != 0);
        view.Window.WindowState = WindowState.Minimized; await view.Canvas.LastSuspension.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(view.Canvas.IsAnimating); int frames = view.Canvas.RenderedFrameCount;
        view.Window.WindowState = WindowState.Normal; await Until(() => view.Canvas.RenderedFrameCount > frames);
    });
    [Fact]
    public Task VirtualAutomationButtonsKeepIdentityBoundsAndInvocationWithoutPerNodeVisuals() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph(sense: true)); await Until(() => view.Canvas.Positions.Count == 2); var canvas = view.Canvas;
        var peer = UIElementAutomationPeer.CreatePeerForElement(canvas)!; Assert.Equal("Vocabulary graph", peer.GetName()); Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        var nodes = peer.GetChildren(); Assert.Equal(2, nodes.Count); Assert.Equal("bank", nodes[0].GetName()); Assert.Equal("bank · Core meaning: 河岸", nodes[1].GetName());
        Assert.Equal(AutomationControlType.Button, nodes[1].GetAutomationControlType());
        Assert.Equal(Id(1).ToString("D") + "/" + Id(2).ToString("D"), nodes[1].GetAutomationId());
        var dpi = VisualTreeHelper.GetDpi(canvas); Assert.InRange(Math.Abs(nodes[0].GetBoundingRectangle().Width - 44 * dpi.DpiScaleX), 0, .01);
        int frames = canvas.RenderedFrameCount; await Until(() => canvas.RenderedFrameCount > frames); Assert.Same(nodes[0], peer.GetChildren()[0]);
        Assert.IsAssignableFrom<IInvokeProvider>(nodes[1].GetPattern(PatternInterface.Invoke)).Invoke(); Assert.Equal(Sense, view.Selections.Last());
        canvas.Update(new(), null, _ => { }); Assert.Empty(peer.GetChildren()); Assert.Throws<ElementNotAvailableException>(() => ((IInvokeProvider)nodes[1].GetPattern(PatternInterface.Invoke)).Invoke());
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(canvas));
    });
    [Fact]
    public Task RetiringDrainsWorkerAndLateAutomationCannotReactivateTheCanvas() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Until(() => view.Canvas.Positions.Count == 2);
        var peer = UIElementAutomationPeer.CreatePeerForElement(view.Canvas)!.GetChildren()[0];
        await view.Canvas.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10)); int frames = view.Canvas.RenderedFrameCount;
        view.Canvas.Update(Graph(sense: true), Sense, _ => { }); view.Canvas.PointerDown(new(1, 1)); view.Canvas.PointerDrag(new(100, 100)); view.Canvas.PointerUp();
        Assert.False(view.Canvas.IsAnimating); Assert.Equal(frames, view.Canvas.RenderedFrameCount);
        Assert.Throws<ElementNotAvailableException>(() => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke());
    });
    [Fact]
    public Task ActualRoutedKeyDownUsesCanvasNavigationBeforeLeavingTheSurface() => dispatcher.RunAsync(async () =>
    {
        await using var view = new View(Graph()); await Until(() => view.Canvas.Positions.Count == 2);
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view.Canvas)!, 0, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent };
        view.Canvas.RaiseEvent(input); Assert.True(input.Handled); Assert.Equal(Head(1), view.Canvas.KeyboardNode);
        var activate = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view.Canvas)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
        view.Canvas.RaiseEvent(activate); Assert.True(activate.Handled); Assert.Contains(Id(1), view.Presentation.Expanded);
    });
    [Fact]
    public Task NativeFirstStrongDirectionHandlesArabicMixedAndSupplementaryLabels() => dispatcher.Run(() =>
    {
        Assert.Equal(WindowsTextDirection.RightToLeft, WindowsTextBaseDirection.Detect("123 · مرحبا (bank)"));
        Assert.Equal(WindowsTextDirection.LeftToRight, WindowsTextBaseDirection.Detect("bank · مرحبا 123"));
        Assert.Equal(WindowsTextDirection.RightToLeft, WindowsTextBaseDirection.Detect("😀 שלום 123"));
        Assert.Equal(WindowsTextDirection.RightToLeft, WindowsTextBaseDirection.Detect("\U0001EE00 123"));
        Assert.Equal(WindowsTextDirection.RightToLeft, WindowsTextBaseDirection.Detect("123\0مرحبا"));
        Assert.Equal(WindowsTextDirection.Neutral, WindowsTextBaseDirection.Detect("123 😀"));
    });
    private static void AssertPoint(VocabularyGraphPoint expected, VocabularyGraphPoint actual)
    { Assert.InRange(Math.Abs(expected.X - actual.X), 0, 1e-8); Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 1e-8); }
}
