using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual UIA client/provider calls over an owned hidden WPF subtree. The synthetic
/// document has TextPattern and no TextEdit or Value pattern. No user focus, text, screen or clipboard.</summary>
[Collection("WPF views")]
public sealed class UiaReadOnlySelectionTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task TextOnlyReadonlyDocumentCanSupplyAnExactSelectionFromItselfOrAnInlineRoot(bool inline) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); using var owner = new UiaTextEditEvidence(() => { });
        string id = await fixture.Id(inline); var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = await Task.Run(() => owner.CaptureOwnedReadingDescendantForTest(fixture.Handle, id, () => true, () => invalidated.TrySetResult()));
        Assert.NotNull(lease); Assert.True(lease.ReadOnly); Assert.Equal("same", lease.Text); Assert.NotNull(lease.SelectionBounds);
        Assert.False(lease.AddedComposition); Assert.False(lease.AddedFinalized); Assert.True(lease.AddedChanged); Assert.True(lease.AddedSelection);
        Assert.True(await Task.Run(() => owner.IsReadingCurrent(lease, () => true)));
        fixture.Document.Start = 9; fixture.Document.End = 13;
        Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => true))); Assert.Equal(0, lease.Active);
        Assert.True(fixture.Document.TextReads > 0); Assert.Equal(0, fixture.Document.Writes);
        owner.RetireReading(lease); await owner.DrainRetainedAsync();
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task EditableOrUnknownReadonlyMetadataWithoutTextEditNeverReadsText(bool unknown) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); fixture.Document.ReadOnly = unknown ? null : false;
        using var owner = new UiaTextEditEvidence(() => { }); string id = await fixture.Id(true);
        Assert.Null(await Task.Run(() => owner.CaptureOwnedReadingDescendantForTest(fixture.Handle, id, () => true, () => { })));
        Assert.Equal(0, fixture.Document.TextReads); Assert.Equal(0, fixture.Document.Writes);
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task AReadonlyDocumentCannotAuthorizeAnEditableOrUnknownSelectedRange(bool unknown) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); fixture.Document.SelectedReadOnly = unknown ? null : false;
        using var owner = new UiaTextEditEvidence(() => { }); string id = await fixture.Id(true);
        Assert.Null(await Task.Run(() => owner.CaptureOwnedReadingDescendantForTest(fixture.Handle, id, () => true, () => { })));
        Assert.Equal(0, fixture.Document.TextReads); Assert.Equal(0, fixture.Document.Writes);
    });
    [Fact]
    public Task MissingFocusCanUseTheIndependentReadonlyPointRootButLaterFocusCannotReuseItsReceipt() => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); using var owner = new UiaTextEditEvidence(() => { });
        string pointId = await fixture.Id(false); string? focused = null;
        var lease = await Task.Run(() => owner.CaptureOwnedReadingRootsForTest(fixture.Handle, () => focused, pointId, () => true, () => { }));
        Assert.NotNull(lease); Assert.True(lease.RequireReadOnlyTarget); Assert.Equal("same", lease.Text);
        Assert.True(await Task.Run(() => owner.IsReadingCurrent(lease, () => true)));
        focused = await fixture.Id(true);
        Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => true))); Assert.Equal(0, lease.Active);
        Assert.Equal(0, fixture.Document.Writes); owner.RetireReading(lease); await owner.DrainRetainedAsync();
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task MissingFocusDoesNotAuthorizeEditableOrUncertainPointText(bool unknown) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); fixture.Document.ReadOnly = unknown ? null : false;
        using var owner = new UiaTextEditEvidence(() => { }); string pointId = await fixture.Id(false);
        Assert.Null(await Task.Run(() => owner.CaptureOwnedReadingRootsForTest(fixture.Handle, () => null, pointId, () => true, () => { })));
        Assert.Equal(0, fixture.Document.TextReads); Assert.Equal(0, fixture.Document.Writes);
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task SensitiveOrUncertainEditableFocusCannotFallThroughToAnotherReadonlyRoot(bool sensitive) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        if (sensitive) AutomationProperties.SetName(fixture.Document, "Password"); else fixture.Document.ReadOnly = false;
        using var owner = new UiaTextEditEvidence(() => { }); string focused = await fixture.Id(false), pointed = await fixture.Id("other");
        Assert.Null(await Task.Run(() => owner.CaptureOwnedReadingRootsForTest(fixture.Handle, () => focused, pointed, () => true, () => { })));
        Assert.Equal(0, fixture.Document.TextReads); Assert.Equal(0, fixture.Other.TextReads); Assert.Equal(0, fixture.Other.Writes);
    });
    [Theory]
    [InlineData("readonly")] [InlineData("geometry")] [InlineData("permit")]
    public Task AChangedReadonlyModeGeometryOrPermitRevokesTheOriginalOccurrence(string change) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(); using var owner = new UiaTextEditEvidence(() => { }); string id = await fixture.Id(true); bool allowed = true;
        var lease = await Task.Run(() => owner.CaptureOwnedReadingDescendantForTest(fixture.Handle, id, () => allowed, () => { })); Assert.NotNull(lease);
        if (change == "readonly") fixture.Document.ReadOnly = false;
        else if (change == "geometry") fixture.Document.Left += 20;
        else allowed = false;
        Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => allowed))); Assert.Equal(0, lease.Active);
        fixture.Document.ReadOnly = true; allowed = true;
        Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => allowed)));
        Assert.Equal(0, fixture.Document.Writes); owner.RetireReading(lease); await owner.DrainRetainedAsync();
    });
    private sealed class Fixture : IDisposable
    {
        private readonly HwndSource _parent = new(new HwndSourceParameters("owned readonly document") { WindowStyle = 0, Width = 240, Height = 80 });
        private readonly HwndSource _child;
        internal readonly Document Document = new();
        internal readonly Document Other = new();
        internal nint Handle => _child.Handle;
        internal Fixture()
        {
            _child = new(new HwndSourceParameters("owned readonly subtree") { ParentWindow = _parent.Handle, WindowStyle = 0x50000000, Width = 240, Height = 80 });
            AutomationProperties.SetAutomationId(Document, "document"); AutomationProperties.SetName(Document, "Article");
            var leaf = new TextBlock { Text = "Synthetic inline", Width = 150, Height = 30 };
            AutomationProperties.SetAutomationId(leaf, "inline"); Document.Content = leaf;
            AutomationProperties.SetAutomationId(Other, "other"); AutomationProperties.SetName(Other, "Other article"); Other.Content = new TextBlock { Text = "Other inline" };
            var panel = new StackPanel(); panel.Children.Add(Document); panel.Children.Add(Other); _child.RootVisual = panel;
            panel.Measure(new Size(240, 80)); panel.Arrange(new Rect(0, 0, 240, 80)); panel.UpdateLayout();
        }
        internal Task<string> Id(bool inline) => Id(inline ? "inline" : "document");
        internal Task<string> Id(string name) => Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(Handle);
            var document = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "document"));
            Assert.NotNull(document); Assert.True(document.TryGetCurrentPattern(TextPattern.Pattern, out _)); Assert.False(document.TryGetCurrentPattern(ValuePattern.Pattern, out _));
            var selected = name == "document" ? document : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, name));
            Assert.NotNull(selected); return UiaFocusTracker.ReadFieldId(selected);
        });
        public void Dispose() { _child.Dispose(); _parent.Dispose(); }
    }
    private sealed class Document : ContentControl
    {
        internal bool? ReadOnly = true, SelectedReadOnly = true; internal int Start, End = 4, TextReads, Writes; internal double Left = 20;
        internal const string Text = "same and same";
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        private sealed class Peer(Document owner) : FrameworkElementAutomationPeer(owner), ITextProvider
        {
            protected override string GetClassNameCore() => "SyntheticDocument";
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
            protected override bool IsControlElementCore() => true;
            protected override bool IsContentElementCore() => true;
            public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Text ? this : base.GetPattern(pattern);
            internal IRawElementProviderSimple Provider => ProviderFromPeer(this);
            public ITextRangeProvider DocumentRange => new Range(owner, this, 0, Text.Length);
            public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
            public ITextRangeProvider[] GetSelection() => [new Range(owner, this, owner.Start, owner.End)];
            public ITextRangeProvider[] GetVisibleRanges() => [DocumentRange];
            public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement) => DocumentRange;
            public ITextRangeProvider RangeFromPoint(Point screenLocation) => DocumentRange;
        }
        private sealed class Range(Document owner, Peer peer, int start, int end) : ITextRangeProvider
        {
            private int _start = start, _end = end;
            public ITextRangeProvider Clone() => new Range(owner, peer, _start, _end);
            public bool Compare(ITextRangeProvider range) => range is Range other && _start == other._start && _end == other._end;
            public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
                => At(endpoint).CompareTo(((Range)targetRange).At(targetEndpoint));
            private int At(TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
            public void ExpandToEnclosingUnit(TextUnit unit) { _start = 0; _end = Text.Length; }
            public ITextRangeProvider? FindAttribute(int attributeId, object value, bool backward) => null;
            public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase) => null;
            public object GetAttributeValue(int attributeId) => attributeId == TextPatternIdentifiers.IsReadOnlyAttribute.Id
                ? (_start == 0 && _end == Text.Length ? owner.ReadOnly : owner.SelectedReadOnly) is bool value ? value : AutomationElement.NotSupported : AutomationElement.NotSupported;
            public double[] GetBoundingRectangles() => _end > _start ? [owner.Left + _start * 8, 20, (_end - _start) * 8, 20] : [];
            public IRawElementProviderSimple GetEnclosingElement() => peer.Provider;
            public string GetText(int maxLength)
            {
                owner.TextReads++; if (maxLength < 0) throw new InvalidOperationException("Reading must be bounded.");
                return Text.Substring(_start, Math.Min(_end - _start, maxLength));
            }
            public int Move(TextUnit unit, int count) => 0;
            public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
            {
                int old = At(endpoint), next = Math.Clamp(old + count, 0, Text.Length);
                if (endpoint == TextPatternRangeEndpoint.Start) { _start = next; _end = Math.Max(_end, _start); }
                else { _end = next; _start = Math.Min(_start, _end); }
                return next - old;
            }
            public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
            {
                int next = ((Range)targetRange).At(targetEndpoint);
                if (endpoint == TextPatternRangeEndpoint.Start) { _start = next; _end = Math.Max(_end, _start); }
                else { _end = next; _start = Math.Min(_start, _end); }
            }
            public void Select() { owner.Writes++; throw new InvalidOperationException("No selection writes."); }
            public void AddToSelection() { owner.Writes++; throw new InvalidOperationException("No selection writes."); }
            public void RemoveFromSelection() { owner.Writes++; throw new InvalidOperationException("No selection writes."); }
            public void ScrollIntoView(bool alignToTop) { owner.Writes++; throw new InvalidOperationException("No scrolling effects."); }
            public IRawElementProviderSimple[] GetChildren() => [];
        }
    }
}
