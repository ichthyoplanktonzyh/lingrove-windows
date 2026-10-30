using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaReadingScreenSecurityTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task OwnedNativeTextEditNeedsASuccessfulEmptyComposition() => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden composition metadata parent") { WindowStyle = 0, Width = 100, Height = 100 });
        nint library = NativeLibrary.Load("msftedit.dll"), edit = 0;
        using var owner = new UiaTextEditEvidence(() => { });
        try
        {
            edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 50, 50, parent.Handle, 0, 0, 0);
            Assert.NotEqual(0, edit);
            ReadingScreenSecurity? receipt = null;
            var capability = await owner.ProbeOwnedHandleForTest(edit,
                element => receipt = UiaTextEditEvidence.ReadOwnedScreenSecurityForTest(element, () => true));
            Assert.True(capability.HasTextEditPattern); Assert.Equal(false, capability.HasActiveComposition);
            Assert.NotNull(receipt);
        }
        finally { if (edit != 0) Assert.True(DestroyWindow(edit)); NativeLibrary.Free(library); }
    });

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ValueWithoutCompositionEvidenceRequiresPositiveReadOnlyMetadata(bool readOnly) => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden readonly parent") { WindowStyle = 0, Width = 100, Height = 100 });
        using var child = new HwndSource(new HwndSourceParameters("Hidden readonly control")
            { ParentWindow = parent.Handle, WindowStyle = 0x50000000, Width = 100, Height = 100 });
        var control = new SyntheticValueControl(readOnly) { Width = 80, Height = 30 };
        AutomationProperties.SetAutomationId(control, "owned-value");
        var panel = new StackPanel(); panel.Children.Add(control); child.RootVisual = panel;
        panel.Measure(new Size(100, 100)); panel.Arrange(new Rect(0, 0, 100, 100)); panel.UpdateLayout();
        nint handle = child.Handle;
        var id = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-value"));
            Assert.NotNull(element); Assert.False(element.TryGetCurrentPattern(TextPattern.Pattern, out _));
            Assert.True(element.TryGetCurrentPattern(ValuePattern.Pattern, out _));
            return UiaFocusTracker.ReadFieldId(element);
        });
        using var owner = new UiaTextEditEvidence(() => { }); ReadingScreenSecurity? receipt = null;
        await owner.ReadOwnedDescendantForTest(handle, id, () => true,
            inspectOwnedElement: element => receipt = UiaTextEditEvidence.ReadOwnedScreenSecurityForTest(element, () => true));
        Assert.Equal(readOnly, receipt is not null);
        Assert.Equal(0, control.ValueReads);
    });

    private sealed class SyntheticValueControl(bool readOnly) : FrameworkElement
    {
        internal int ValueReads;
        protected override AutomationPeer OnCreateAutomationPeer() => new ValuePeer(this, readOnly);
        private sealed class ValuePeer(SyntheticValueControl owner, bool readOnly) : FrameworkElementAutomationPeer(owner), IValueProvider
        {
            protected override string GetClassNameCore() => "SyntheticValue";
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
            protected override bool IsControlElementCore() => true;
            protected override bool IsContentElementCore() => true;
            public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Value ? this : base.GetPattern(pattern);
            public bool IsReadOnly => readOnly;
            public string Value { get { owner.ValueReads++; throw new InvalidOperationException("Metadata capture must never read a value."); } }
            public void SetValue(string value) => throw new InvalidOperationException("Metadata capture has no write authority.");
        }
    }
    [Fact]
    public Task MetadataAdmitsAnOwnedNonTextControlAndRejectsAnOwnedPasswordWithoutReadingItsValue() => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden security parent") { WindowStyle = 0, Width = 100, Height = 100 });
        using var child = new HwndSource(new HwndSourceParameters("Hidden security controls")
            { ParentWindow = parent.Handle, WindowStyle = 0x50000000, Width = 100, Height = 100 });
        var button = new Button { Content = "Synthetic label" }; var password = new PasswordBox { Password = "test-only" };
        AutomationProperties.SetAutomationId(button, "owned-static"); AutomationProperties.SetAutomationId(password, "owned-password");
        var panel = new StackPanel(); panel.Children.Add(button); panel.Children.Add(password); child.RootVisual = panel;
        panel.Measure(new Size(100, 100)); panel.Arrange(new Rect(0, 0, 100, 100)); panel.UpdateLayout();
        nint handle = child.Handle;
        var ids = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var staticElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-static"));
            var secureElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-password"));
            Assert.NotNull(staticElement); Assert.NotNull(secureElement);
            Assert.False(staticElement.TryGetCurrentPattern(TextPattern.Pattern, out _));
            return (UiaFocusTracker.ReadFieldId(staticElement), UiaFocusTracker.ReadFieldId(secureElement));
        });
        using var owner = new UiaTextEditEvidence(() => { }); ReadingScreenSecurity? safe = null, secure = null;
        await owner.ReadOwnedDescendantForTest(handle, ids.Item1, () => true,
            inspectOwnedElement: element => safe = UiaTextEditEvidence.ReadOwnedScreenSecurityForTest(element, () => true));
        await owner.ReadOwnedDescendantForTest(handle, ids.Item2, () => true,
            inspectOwnedElement: element => secure = UiaTextEditEvidence.ReadOwnedScreenSecurityForTest(element, () => true));
        Assert.NotNull(safe); Assert.Equal(ids.Item1, safe.RuntimeId); Assert.Null(secure);
    });
}
