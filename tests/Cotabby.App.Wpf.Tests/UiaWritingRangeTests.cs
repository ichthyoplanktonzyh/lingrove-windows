using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaWritingRangeTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData("same. same.", 6, 5, 8, 0)]
    [InlineData("前。你好😀。后。", 2, 5, 3, 0)]
    [InlineData("a café é. tail", 2, 8, 5, 0)]
    [InlineData("前。你好😀。后。", 2, 5, 2, 5)]
    public Task NativePrefixAnchorsSelectOnlyTheExactUnicodeOccurrence(string text, int start, int length, int caret, int selectionLength) =>
        dispatcher.RunAsync(async () =>
        {
            using var fixture = new OwnedEditor(text, caret, selectionLength);
            using var owner = new UiaTextEditEvidence(() => { });
            string id = await fixture.RuntimeIdAsync(); var saved = await Capture(owner, fixture, id);
            Assert.True(saved.SupportsRangeReplacement);
            int writes = 0; var outcome = RangeEditDispatch.Rejected;
            await owner.ReadOwnedDescendantForTest(fixture.Handle, id, () => true, inspectOwnedElement: element =>
            {
                outcome = UiaTextEditEvidence.SelectOwnedWritingRangeForTest(element, saved, new(start, length), text.Substring(start, length),
                    () => true, permit =>
                    {
                        if (!permit()) return false;
                        fixture.Box.Dispatcher.Invoke(() =>
                        {
                            Assert.Equal(start, fixture.Box.SelectionStart); Assert.Equal(length, fixture.Box.SelectionLength);
                            Assert.Equal(text.Substring(start, length), fixture.Box.SelectedText);
                            fixture.Box.SelectedText = "译文"; writes++;
                        });
                        return true;
                    }, () => true);
            });
            Assert.Equal(RangeEditDispatch.Dispatched, outcome); Assert.Equal(1, writes);
            Assert.Equal(text[..start] + "译文" + text[(start + length)..], fixture.Box.Text);
        });

    [Theory]
    [InlineData("readonly")]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("permit")]
    public Task InvalidNativeEvidenceDoesNotSelectOrWrite(string change) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new OwnedEditor("same. same.", 8, 0);
        using var owner = new UiaTextEditEvidence(() => { }); string id = await fixture.RuntimeIdAsync();
        var saved = await Capture(owner, fixture, id);
        if (change == "readonly") fixture.Box.IsReadOnly = true;
        if (change == "source") fixture.Box.Text = "same. other";
        if (change == "selection") fixture.Box.Select(0, 5);
        int writes = 0; var outcome = RangeEditDispatch.Uncertain;
        await owner.ReadOwnedDescendantForTest(fixture.Handle, id, () => true, inspectOwnedElement: element =>
            outcome = UiaTextEditEvidence.SelectOwnedWritingRangeForTest(element, saved, new(6, 5), "same.",
                () => change != "permit", _ => { writes++; return true; }, () => true));
        Assert.Equal(RangeEditDispatch.Rejected, outcome); Assert.Equal(0, writes);
        Assert.Equal(change == "selection" ? 0 : change == "source" ? 0 : 8, fixture.Box.SelectionStart);
    });

    [Fact]
    public Task SelectionDriftAtFinalDispatchRevokesItsNativePermit() => dispatcher.RunAsync(async () =>
    {
        using var fixture = new OwnedEditor("same. same.", 8, 0);
        using var owner = new UiaTextEditEvidence(() => { }); string id = await fixture.RuntimeIdAsync();
        var saved = await Capture(owner, fixture, id); int writes = 0; var outcome = RangeEditDispatch.Rejected;
        await owner.ReadOwnedDescendantForTest(fixture.Handle, id, () => true, inspectOwnedElement: element =>
            outcome = UiaTextEditEvidence.SelectOwnedWritingRangeForTest(element, saved, new(6, 5), "same.", () => true, permit =>
            {
                fixture.Box.Dispatcher.Invoke(() => fixture.Box.Select(0, 5));
                if (!permit()) return false;
                writes++; return true;
            }, () => true));
        Assert.Equal(RangeEditDispatch.Uncertain, outcome); Assert.Equal(0, writes); Assert.Equal("same. same.", fixture.Box.Text);
    });

    private static async Task<FocusedField> Capture(UiaTextEditEvidence owner, OwnedEditor fixture, string id)
    {
        var read = await owner.ReadOwnedDescendantForTest(fixture.Handle, id, () => true);
        var window = Assert.IsType<NativeTextRangeReader.Window>(read.Window);
        return new() { ProcessId = Environment.ProcessId, ProcessName = "owned-component", FieldId = id,
            Text = window.Text, CaretOffset = window.Caret, HasSelection = window.HasSelection, SelectionLength = window.SelectionLength,
            HasReliableSelection = window.ReliableSelection, IsTextWindowTruncated = window.Truncated,
            SupportsRangeReplacement = window.SupportsRangeReplacement, Composition = InputCompositionState.Clear,
            CompositionSequence = owner.Version, FocusChangeSequence = 1, IsSecure = false, IsSingleLine = false,
            CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty };
    }

    private sealed class OwnedEditor : IDisposable
    {
        private readonly HwndSource _parent, _child;
        internal TextBox Box { get; }
        internal nint Handle => _child.Handle;
        internal OwnedEditor(string text, int caret, int selectionLength)
        {
            _parent = new(new HwndSourceParameters("Hidden range parent") { WindowStyle = 0, Width = 120, Height = 80 });
            _child = new(new HwndSourceParameters("Hidden owned range editor") { ParentWindow = _parent.Handle, WindowStyle = 0x50000000, Width = 120, Height = 80 });
            Box = new() { Text = text }; AutomationProperties.SetAutomationId(Box, "owned-range");
            var panel = new StackPanel(); panel.Children.Add(Box); _child.RootVisual = panel;
            panel.Measure(new Size(120, 80)); panel.Arrange(new Rect(0, 0, 120, 80)); panel.UpdateLayout(); Box.Select(caret, selectionLength);
        }
        internal Task<string> RuntimeIdAsync()
        {
            nint handle = Handle;
            return Task.Run(() => UiaFocusTracker.ReadFieldId(AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-range"))));
        }
        public void Dispose() { _child.Dispose(); _parent.Dispose(); }
    }
}
