using System.Runtime.Versioning;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Cotabby.Win32.Focus;
using Xunit.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaOwnedBootstrapTests(WpfDispatcher dispatcher, ITestOutputHelper output)
{
    [Fact]
    public Task OwnedHiddenWindowAgreesOnNativeAndManagedRuntimeIdentity() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        var parameters = new HwndSourceParameters("Lingrove UIA owned test") { WindowStyle = 0, Width = 32, Height = 32 };
        using var source = new HwndSource(parameters) { RootVisual = new TextBox() };
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            var bootstrap = await owner.BootstrapForTest.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(bootstrap.HasAutomation, $"Factory HRESULT: 0x{bootstrap.FactoryHResult:X8}");
            nint handle = source.Handle;
            string managed = await Task.Run(() => UiaFocusTracker.ReadFieldId(AutomationElement.FromHandle(handle)));
            var native = await owner.ProbeOwnedHandleForTest(handle).WaitAsync(TimeSpan.FromSeconds(5));
            output.WriteLine($"ElementFromHandle=0x{native.ElementHResult:X8}; TextEdit pattern HRESULT=0x{native.PatternHResult:X8}; pattern present={native.HasTextEditPattern}");
            output.WriteLine($"Composition add/remove=0x{native.CompositionRegistrationHResult:X8}/0x{native.CompositionRemovalHResult:X8}; finalized add/remove=0x{native.FinalizedRegistrationHResult:X8}/0x{native.FinalizedRemovalHResult:X8}");
            Assert.True(native.ElementHResult >= 0, $"ElementFromHandle=0x{native.ElementHResult:X8}");
            Assert.False(string.IsNullOrWhiteSpace(managed));
            Assert.Equal(managed, native.RuntimeId);
            Assert.True(native.CompositionRegistrationHResult is >= 0 && native.CompositionRemovalHResult is >= 0);
            Assert.True(native.FinalizedRegistrationHResult is >= 0 && native.FinalizedRemovalHResult is >= 0);
            // Pattern absence is reported as such: this identity check does not assert Clear.
        }
        finally { await Task.Run(owner.Dispose); }
    });
}
