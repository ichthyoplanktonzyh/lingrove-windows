using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Automation;
using System.Windows.Interop;
using Cotabby.Core.Focus;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaRetainedCompositionTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task HiddenOwnedRichEditTextChangeRetiresRetainedReceipt() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var source = new HwndSource(new HwndSourceParameters("Hidden retained event test") { WindowStyle = 0, Width = 50, Height = 50 });
        nint library = 0, edit = 0;
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            library = LoadLibrary("msftedit.dll"); Assert.NotEqual(0, library);
            edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, source.Handle, 0, 0, 0);
            Assert.NotEqual(0, edit);
            nint owned = edit;
            string field = await Task.Run(() => UiaFocusTracker.ReadFieldId(AutomationElement.FromHandle(owned)));
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long? token = await owner.RegisterOwnedRetainedForTest(owned, field, () => changed.TrySetResult()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(token);
            Assert.True(SetWindowText(edit, "owned test text"));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(default, await owner.ObserveOwnedRetainedForTest(token.Value).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await Task.Run(owner.Dispose);
            bool destroyed = edit == 0 || DestroyWindow(edit);
            bool freed = library == 0 || FreeLibrary(library);
            Assert.True(destroyed && freed);
        }
    });

    [Fact]
    public Task HiddenOwnedRichEditRetainsAndRetiresNativeSubscription() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var source = new HwndSource(new HwndSourceParameters("Hidden retained test") { WindowStyle = 0, Width = 50, Height = 50 });
        nint library = 0, edit = 0, callback = 0;
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            library = LoadLibrary("msftedit.dll");
            Assert.NotEqual(0, library);
            edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, source.Handle, 0, 0, 0);
            Assert.NotEqual(0, edit);
            nint owned = edit;
            string field = await Task.Run(() => UiaFocusTracker.ReadFieldId(AutomationElement.FromHandle(owned)));
            Assert.False(string.IsNullOrEmpty(field));
            int changed = 0;
            long? token = await owner.RegisterOwnedRetainedForTest(owned, field, () => Interlocked.Increment(ref changed)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(token);
            var first = await owner.ObserveOwnedRetainedForTest(token.Value).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(InputCompositionState.Clear, first.Item1);
            callback = owner.CreateRetainedCallbackForTest(token.Value);
            UiaTextEditEvidence.SignalCallbackForTest(callback);
            Assert.Equal(1, changed);
            var second = await owner.ObserveOwnedRetainedForTest(token.Value).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(default, second);
            owner.RetireRetainedHost(token.Value);
            UiaTextEditEvidence.SignalCallbackForTest(callback);
            Assert.Equal(1, changed);
            Assert.Equal(default, await owner.ObserveOwnedRetainedForTest(token.Value).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (callback != 0) Marshal.Release(callback);
            await Task.Run(owner.Dispose);
            bool destroyed = edit == 0 || DestroyWindow(edit);
            bool freed = library == 0 || FreeLibrary(library);
            Assert.True(destroyed && freed);
        }
    });

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint handle);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowText(nint handle, string text);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(nint module);
}
