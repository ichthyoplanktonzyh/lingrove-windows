using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.ExpressionAssist;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Hidden native RichEdit only; UIA selects its range and the callback edits that owned
/// control by synchronous message. No activation, global input, clipboard or user editor access.</summary>
[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaExpressionRangeCommitTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData("？？", "X", false)]
    [InlineData("？？", "", false)]
    [InlineData("😀😀", "日", false)]
    [InlineData("？？", "X", true)]
    [InlineData("??", "X", false)]
    [InlineData("selected", "X", false)]
    [InlineData("", "X", false)]
    public Task OwnedRangeCommitConsumesItsNotificationReceiptAndAcknowledgesExactValue(string trigger, string replacement, bool partial) => dispatcher.RunAsync(async () =>
    {
        using var fixture = new NativeFixture();
        string prefix = "repeat？？ / before"; string authored = prefix + trigger + "after";
        bool selected = trigger == "selected";
        int caret = selected ? prefix.Length : prefix.Length + trigger.Length;
        Assert.True(SetWindowText(fixture.Edit, authored)); SetSelection(fixture.Edit, 0x00B1, caret, prefix.Length + trigger.Length);
        await using var lifetime = new NativeOwner(); var owner = lifetime.Native;
        NativeTextRangeReader.ValueSelection? original = null;
        var info = await owner.ProbeOwnedHandleForTest(fixture.Edit,
            element => original = UiaTextEditEvidence.ReadOwnedExpressionValueForTest(element));
        Assert.NotNull(original); Assert.Equal(caret, original.Value.Start); Assert.Equal(selected ? trigger.Length : 0, original.Value.Length);
        int invalidated = 0;
        var token = await owner.RegisterOwnedRetainedForTest(fixture.Edit, info.RuntimeId!, () => Interlocked.Increment(ref invalidated)); Assert.NotNull(token);
        var composition = await owner.ObserveOwnedRetainedForTest(token.Value); Assert.Equal(InputCompositionState.Clear, composition.Item1);
        var saved = Field(info.RuntimeId!, original.Value, owner.Version);
        var range = new LearningTextRange(prefix.Length, trigger.Length);
        string expected = original.Value.Value.Remove(range.Location, range.Length).Insert(range.Location, replacement);
        int dispatched = 0;
        var result = await owner.ReplaceOwnedExpressionRangeForTest(token.Value, composition.Item2, saved, range, trigger, expected,
            () => true, permit =>
            {
                Assert.True(permit()); Interlocked.Increment(ref dispatched);
                ReplaceSelection(fixture.Edit, 0x00C2, 1, replacement); return !partial;
            });
        Assert.Equal(partial ? ExpressionEditOutcome.Unverified : ExpressionEditOutcome.Verified, result);
        Assert.Equal(1, dispatched); Assert.Equal(0, invalidated);
        Assert.False(owner.IsRetainedReceiptCurrent(token.Value, composition.Item2));
        Assert.Equal(ExpressionEditOutcome.Rejected, await owner.ReplaceOwnedExpressionRangeForTest(token.Value, composition.Item2,
            saved, range, trigger, expected, () => true, _ => throw new InvalidOperationException("No retry")));
    });

    [Fact]
    public Task RetiredNativeReceiptCannotSelectOrDispatch() => dispatcher.RunAsync(async () =>
    {
        using var fixture = new NativeFixture(); Assert.True(SetWindowText(fixture.Edit, "before？？after"));
        SetSelection(fixture.Edit, 0x00B1, 8, 8);
        await using var lifetime = new NativeOwner(); var owner = lifetime.Native;
        NativeTextRangeReader.ValueSelection? original = null;
        var info = await owner.ProbeOwnedHandleForTest(fixture.Edit, element => original = UiaTextEditEvidence.ReadOwnedExpressionValueForTest(element));
        Assert.NotNull(original);
        var token = await owner.RegisterOwnedRetainedForTest(fixture.Edit, info.RuntimeId!, () => { }); Assert.NotNull(token);
        var state = await owner.ObserveOwnedRetainedForTest(token.Value); owner.RetireRetainedHost(token.Value);
        Assert.Equal(ExpressionEditOutcome.Rejected, await owner.ReplaceOwnedExpressionRangeForTest(token.Value, state.Item2,
            Field(info.RuntimeId!, original.Value, owner.Version), new(6, 2), "？？", "beforeXafter", () => true,
            _ => throw new InvalidOperationException("Retired receipt cannot edit")));
    });

    private static FocusedField Field(string id, NativeTextRangeReader.ValueSelection value, long version) => new()
    {
        ProcessId = Environment.ProcessId, ProcessName = "owned-rich-edit", FieldId = id, Text = value.Value,
        CaretOffset = value.Start, SelectionLength = value.Length, HasSelection = value.Length > 0,
        HasReliableSelection = true, SupportsRangeReplacement = true, Composition = InputCompositionState.Clear,
        CompositionSequence = version, FocusChangeSequence = 1, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty,
        IsSingleLine = false, IsSecure = false,
    };
    private sealed class NativeFixture : IDisposable
    {
        private readonly HwndSource _parent = new(new HwndSourceParameters("Hidden expression range parent") { WindowStyle = 0, Width = 100, Height = 100 });
        private readonly nint _library = NativeLibrary.Load("msftedit.dll");
        internal nint Edit { get; }
        internal NativeFixture()
        {
            Edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 50, 50, _parent.Handle, 0, 0, 0);
            Assert.NotEqual(0, Edit);
        }
        public void Dispose() { bool destroyed = DestroyWindow(Edit); NativeLibrary.Free(_library); _parent.Dispose(); Assert.True(destroyed); }
    }
    private sealed class NativeOwner : IAsyncDisposable
    {
        internal readonly UiaTextEditEvidence Native = new(() => { });
        // Provider release can message the owned UI thread. Keep its dispatcher pumping while
        // the same production MTA owner joins native callbacks and releases retained interfaces.
        public ValueTask DisposeAsync() => new(Task.Run(Native.Dispose));
    }
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint handle);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowText(nint handle, string text);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SetSelection(nint handle, uint message, nint start, nint end);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint ReplaceSelection(nint handle, uint message, nint undo, string text);
}
