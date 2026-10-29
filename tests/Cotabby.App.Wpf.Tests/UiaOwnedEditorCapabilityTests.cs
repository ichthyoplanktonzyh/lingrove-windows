using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Win32.Focus;
using Xunit.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaOwnedEditorCapabilityTests(WpfDispatcher dispatcher, ITestOutputHelper output)
{
    [Fact]
    public Task ReportsOwnedEditorCapabilitiesWithoutFocusingThem() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        var parameters = new HwndSourceParameters("Lingrove owned editor capability test") { WindowStyle = 0, Width = 80, Height = 80 };
        var textBox = new TextBox();
        AutomationProperties.SetAutomationId(textBox, "owned-wpf-editor");
        using var source = new HwndSource(parameters);
        // The child is WS_VISIBLE inside an invisible parent; neither HWND is shown or activated.
        var childParameters = new HwndSourceParameters("Lingrove hidden WPF child")
        { ParentWindow = source.Handle, WindowStyle = 0x50000000, Width = 80, Height = 80 };
        var panel = new StackPanel();
        panel.Children.Add(textBox);
        using var wpfSource = new HwndSource(childParameters) { RootVisual = panel };
        panel.Measure(new Size(80, 80));
        panel.Arrange(new Rect(0, 0, 80, 80));
        panel.UpdateLayout();
        _ = UIElementAutomationPeer.CreatePeerForElement(panel);
        _ = UIElementAutomationPeer.CreatePeerForElement(textBox);
        nint richLibrary = 0, edit = 0, rich = 0;
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            richLibrary = LoadLibrary("msftedit.dll");
            edit = CreateWindowEx(0, "EDIT", null, 0x40000000, 0, 0, 20, 20, source.Handle, 0, 0, 0);
            rich = richLibrary == 0 ? 0 : CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, source.Handle, 0, 0, 0);
            Assert.NotEqual(0, edit);
            Assert.NotEqual(0, richLibrary);
            Assert.NotEqual(0, rich);
            Assert.True((await owner.BootstrapForTest.WaitAsync(TimeSpan.FromSeconds(5))).HasAutomation);
            await ProbeWindow("EDIT", edit, owner);
            await ProbeWindow("RichEdit", rich, owner);
            nint host = wpfSource.Handle;
            var wpfLookup = await Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(host);
                var child = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "owned-wpf-editor"));
                return (RootId: root.Current.AutomationId, RootType: root.Current.ControlType.ProgrammaticName,
                    ChildId: child is null ? null : UiaFocusTracker.ReadFieldId(child));
            });
            output.WriteLine($"WPF host UIA root: automation-id={wpfLookup.RootId}, type={wpfLookup.RootType}");
            string? wpfId = wpfLookup.ChildId;
            Assert.False(string.IsNullOrEmpty(wpfId), "The hidden WPF TextBox descendant was unavailable in the UIA tree.");
            var wpf = await owner.ProbeOwnedDescendantForTest(host, wpfId!).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(wpf.HasValue, "Native UIA did not find the managed WPF descendant within 64 elements.");
            Assert.Equal(wpfId, wpf.Value.RuntimeId);
            Report("WPF TextBox descendant", wpf.Value);
        }
        finally
        {
            await Task.Run(owner.Dispose);
            bool richDestroyed = rich == 0 || DestroyWindow(rich);
            bool editDestroyed = edit == 0 || DestroyWindow(edit);
            bool libraryFreed = richLibrary == 0 || FreeLibrary(richLibrary);
            Assert.True(richDestroyed && editDestroyed && libraryFreed, "Owned editor resources did not all release.");
        }
    });

    private async Task ProbeWindow(string label, nint handle, UiaTextEditEvidence owner)
    {
        string managed = await Task.Run(() => UiaFocusTracker.ReadFieldId(AutomationElement.FromHandle(handle)));
        var native = await owner.ProbeOwnedHandleForTest(handle).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(native.ElementHResult >= 0);
        Assert.False(string.IsNullOrWhiteSpace(managed));
        Assert.Equal(managed, native.RuntimeId);
        Report(label, native);
    }

    private void Report(string label, UiaOwnedElementResult value)
    {
        output.WriteLine($"{label}: element=0x{value.ElementHResult:X8}; pattern=0x{value.PatternHResult:X8}; present={value.HasTextEditPattern}; active-result={value.ActiveCompositionHResult?.ToString("X8") ?? "not queried"}; active={value.HasActiveComposition?.ToString() ?? "unknown"}; composition add/remove={value.CompositionRegistrationHResult?.ToString("X8")}/{value.CompositionRemovalHResult?.ToString("X8")}; finalized add/remove={value.FinalizedRegistrationHResult?.ToString("X8")}/{value.FinalizedRemovalHResult?.ToString("X8")}");
        Assert.True(value.CompositionRegistrationHResult is >= 0 && value.CompositionRemovalHResult is >= 0);
        Assert.True(value.FinalizedRegistrationHResult is >= 0 && value.FinalizedRemovalHResult is >= 0);
        if (!value.HasTextEditPattern) Assert.Null(value.ActiveCompositionHResult);
        else Assert.True(value.ActiveCompositionHResult is >= 0 && value.HasActiveComposition.HasValue,
            $"TextEdit query failed: 0x{value.ActiveCompositionHResult:X8}");
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint handle);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
