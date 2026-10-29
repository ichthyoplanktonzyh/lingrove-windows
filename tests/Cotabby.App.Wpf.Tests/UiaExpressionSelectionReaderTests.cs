using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Win32.ExpressionAssist;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class UiaExpressionSelectionReaderTests(WpfDispatcher dispatcher)
{
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
        nint handle = child.Handle;
        var results = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var largeElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-large"));
            var secretElement = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-secret"));
            Assert.NotNull(largeElement); Assert.NotNull(secretElement);
            return (UiaExpressionSelectionReader.Read(largeElement), UiaExpressionSelectionReader.Read(secretElement));
        });
        Assert.Null(results.Item1);
        Assert.Null(results.Item2);
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
        var selection = await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(handle);
            var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-selection"));
            return element is null ? null : UiaExpressionSelectionReader.Read(element);
        });
        Assert.NotNull(selection);
        Assert.Equal("a😀b!", selection.Value.Value);
        Assert.Equal(new ExpressionEditorRange(1, 2), selection.Value.Selection);
    });
}
