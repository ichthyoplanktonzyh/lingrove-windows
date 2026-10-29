using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Runtime.Versioning;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Win32.ExpressionAssist;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaExpressionSelectionReaderTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task HiddenRichTextWithEmbeddedControlCannotProduceReceipt() => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden parent") { WindowStyle = 0, Width = 80, Height = 80 });
        using var child = new HwndSource(new HwndSourceParameters("Hidden rich editor")
        { ParentWindow = parent.Handle, WindowStyle = 0x50000000, Width = 80, Height = 80 });
        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("A"));
        paragraph.Inlines.Add(new InlineUIContainer(new Button { Content = "owned" }));
        paragraph.Inlines.Add(new Run("B"));
        var rich = new RichTextBox(new FlowDocument(paragraph));
        AutomationProperties.SetAutomationId(rich, "owned-embedded");
        var panel = new StackPanel(); panel.Children.Add(rich); child.RootVisual = panel;
        panel.Measure(new Size(80, 80)); panel.Arrange(new Rect(0, 0, 80, 80)); panel.UpdateLayout();
        nint handle = child.Handle;
        var id = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-embedded"));
            Assert.NotNull(element);
            Assert.True(element.TryGetCurrentPattern(TextPattern.Pattern, out var raw));
            Assert.NotEmpty(((TextPattern)raw).DocumentRange.GetChildren());
            return UiaFocusTracker.ReadFieldId(element);
        });
        using var owner = new UiaTextEditEvidence(() => { });
        var result = await owner.ReadOwnedDescendantForTest(handle, id, () => true);
        Assert.Null(result.Selection);
    });

    [Fact]
    public Task HiddenOversizedAndPasswordFieldsAreRejected() => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden parent") { WindowStyle = 0, Width = 80, Height = 80 });
        using var child = new HwndSource(new HwndSourceParameters("Hidden WPF editor")
        { ParentWindow = parent.Handle, WindowStyle = 0x50000000, Width = 80, Height = 80 });
        var large = new TextBox { Text = new string('a', ExpressionEditorReceiptPolicy.MaximumValueUtf16Units + 1) };
        var secret = new PasswordBox { Password = "test-only" };
        AutomationProperties.SetAutomationId(large, "owned-large");
        AutomationProperties.SetAutomationId(secret, "owned-secret");
        var panel = new StackPanel(); panel.Children.Add(large); panel.Children.Add(secret); child.RootVisual = panel;
        panel.Measure(new Size(80, 80)); panel.Arrange(new Rect(0, 0, 80, 80)); panel.UpdateLayout();
        large.Select(large.Text.Length, 0);
        nint handle = child.Handle;
        var results = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var largeElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-large"));
            var secretElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-secret"));
            Assert.NotNull(largeElement); Assert.NotNull(secretElement);
            return (UiaFocusTracker.ReadFieldId(largeElement), UiaExpressionSelectionReader.IsSafe(secretElement));
        });
        using var owner = new UiaTextEditEvidence(() => { });
        var oversized = await owner.ReadOwnedDescendantForTest(handle, results.Item1, () => true);
        Assert.Null(oversized.Selection);
        Assert.Null(oversized.Window);
        large.Select(20_000, 0);
        var bounded = await owner.ReadOwnedDescendantForTest(handle, results.Item1, () => true);
        Assert.Null(bounded.Selection); // Full value remains oversized.
        Assert.NotNull(bounded.Window);
        Assert.Equal(Cotabby.Core.Focus.FocusTextWindowPolicy.MaxCharsBeforeCaret, bounded.Window.Value.Caret);
        Assert.True(bounded.Window.Value.Truncated);
        Assert.False(results.Item2);
    });

    [Fact]
    public Task HiddenOwnedTextBoxReadsExactValueAndSelectedRange() => dispatcher.RunAsync(async () =>
    {
        using var parent = new HwndSource(new HwndSourceParameters("Hidden parent") { WindowStyle = 0, Width = 80, Height = 80 });
        using var child = new HwndSource(new HwndSourceParameters("Hidden WPF editor")
        { ParentWindow = parent.Handle, WindowStyle = 0x50000000, Width = 80, Height = 80 });
        var box = new TextBox { Text = "a😀b!" };
        var panel = new StackPanel(); panel.Children.Add(box); child.RootVisual = panel;
        panel.Measure(new Size(80, 80)); panel.Arrange(new Rect(0, 0, 80, 80)); panel.UpdateLayout();
        AutomationProperties.SetAutomationId(box, "owned-selection");
        box.Select(1, 2);
        nint handle = child.Handle;
        var id = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-selection"));
            return element is null ? null : UiaFocusTracker.ReadFieldId(element);
        });
        Assert.NotNull(id);
        using var owner = new UiaTextEditEvidence(() => { });
        var read = await owner.ReadOwnedDescendantForTest(handle, id, () => true);
        var selection = read.Selection;
        Assert.NotNull(selection);
        Assert.Equal("a😀b!", selection.Value.Value);
        Assert.Equal(1, selection.Value.Start);
        Assert.Equal(2, selection.Value.Length);
        Assert.NotNull(read.Window);
        Assert.Equal("ab!", read.Window.Value.Text);
        Assert.Equal(1, read.Window.Value.Caret);
        Assert.True(read.Window.Value.HasSelection);
        box.Select(box.Text.Length, 0);
        var caret = await owner.ReadOwnedDescendantForTest(handle, id, () => true);
        Assert.NotNull(caret.Selection);
        Assert.Equal(box.Text.Length, caret.Selection.Value.Start);
        Assert.Equal(0, caret.Selection.Value.Length);
        Assert.NotNull(caret.Window);
        Assert.Equal(box.Text, caret.Window.Value.Text);
        int checks = 0;
        var revoked = await owner.ReadOwnedDescendantForTest(handle, id, () => true, () => ++checks < 12);
        Assert.Null(revoked.Selection);
        Assert.Null(revoked.Window);
        Assert.True(checks >= 12);
    });
}
