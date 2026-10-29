using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
[SupportedOSPlatform("windows8.1")]
public sealed class UiaReadingSelectionTests(WpfDispatcher dispatcher)
{
    [Fact]
    public void ReadingControlGateRejectsIntegratedTerminalAndCredentialLabels()
    {
        Assert.False(UiaTextEditEvidence.ReadingControlAllowed("", "", "", "xterm-helper-textarea"));
        Assert.False(UiaTextEditEvidence.ReadingControlAllowed("Password", "", "", "RichEdit"));
        Assert.True(UiaTextEditEvidence.ReadingControlAllowed("Article", "", "Text", "RichEdit"));
    }
    [Fact]
    public Task OwnedHiddenRichEditKeepsExactReadOnlyOccurrence() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var window = new HwndSource(new HwndSourceParameters("owned reading fixture") { WindowStyle = 0, Width = 40, Height = 40 });
        nint library = LoadLibrary("msftedit.dll");
        Assert.NotEqual(0, library);
        nint edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, window.Handle, 0, 0, 0);
        Assert.NotEqual(0, edit);
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            Assert.True(SetWindowText(edit, "same and same"));
            SendMessage(edit, 0x00B1, 0, 4); // EM_SETSEL on the owned hidden fixture only.
            SendMessage(edit, 0x00CF, 1, 0); // EM_SETREADONLY: reading does not require edit authority.
            nint owned = edit;
            var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lease = await Task.Run(() => owner.CaptureReading(owned, Environment.ProcessId, () => true, () => invalidated.TrySetResult(), ownedTest: true));
            Assert.NotNull(lease);
            Assert.Equal("same", lease.Text);
            Assert.True(await Task.Run(() => owner.IsReadingCurrent(lease, () => true)));
            SendMessage(edit, 0x00B1, 9, 13);
            Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => true)));
            await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            SendMessage(edit, 0x00B1, 0, 4);
            Assert.False(await Task.Run(() => owner.IsReadingCurrent(lease, () => true)));
            owner.RetireReading(lease);
            await owner.DrainRetainedAsync();
        }
        finally
        {
            await Task.Run(owner.Dispose);
            Assert.True(DestroyWindow(edit));
            Assert.True(FreeLibrary(library));
        }
    });

    [Fact]
    public Task RevokedQueuedOwnedCaptureNeverOpensAReadingLease() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var window = new HwndSource(new HwndSourceParameters("owned queued reading") { WindowStyle = 0, Width = 40, Height = 40 });
        nint library = LoadLibrary("msftedit.dll"); Assert.NotEqual(0, library);
        nint edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, window.Handle, 0, 0, 0); Assert.NotEqual(0, edit);
        var owner = new UiaTextEditEvidence(() => { });
        using var release = new ManualResetEventSlim();
        bool allowed = true;
        try
        {
            Assert.True(SetWindowText(edit, "owned")); SendMessage(edit, 0x00B1, 0, -1);
            Assert.False(owner.Execute(() => release.Wait()));
            nint owned = edit;
            var capture = Task.Run(() => owner.CaptureReading(owned, Environment.ProcessId, () => Volatile.Read(ref allowed), () => { }, ownedTest: true));
            Volatile.Write(ref allowed, false);
            release.Set();
            Assert.Null(await capture.WaitAsync(TimeSpan.FromSeconds(5)));
            await owner.DrainRetainedAsync();
        }
        finally
        {
            release.Set(); await Task.Run(owner.Dispose);
            Assert.True(DestroyWindow(edit)); Assert.True(FreeLibrary(library));
        }
    });

    [Fact]
    public Task OwnedSelectionRejectsOversizedUtf8AndRevokedPermit() => dispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var window = new HwndSource(new HwndSourceParameters("owned reading limit") { WindowStyle = 0, Width = 40, Height = 40 });
        nint library = LoadLibrary("msftedit.dll"); Assert.NotEqual(0, library);
        nint edit = CreateWindowEx(0, "RICHEDIT50W", null, 0x40000000, 0, 0, 20, 20, window.Handle, 0, 0, 0); Assert.NotEqual(0, edit);
        var owner = new UiaTextEditEvidence(() => { });
        try
        {
            Assert.True(SetWindowText(edit, new string('日', 90_000)));
            Assert.Equal(90_000, GetWindowTextLength(edit));
            SendMessage(edit, 0x00B1, 0, -1);
            nint owned = edit;
            var oversized = await Task.Run(() => owner.CaptureReading(owned, Environment.ProcessId, () => true, () => { }, ownedTest: true));
            Assert.Null(oversized);
            await owner.DrainRetainedAsync();
            Assert.True(SetWindowText(edit, "permitted"));
            SendMessage(edit, 0x00B1, 0, -1);
            int checks = 0;
            var revoked = await Task.Run(() => owner.CaptureReading(owned, Environment.ProcessId,
                () => Interlocked.Increment(ref checks) < 8, () => { }, ownedTest: true));
            Assert.Null(revoked);
            Assert.True(checks >= 8);
            await owner.DrainRetainedAsync();
        }
        finally
        {
            await Task.Run(owner.Dispose);
            Assert.True(DestroyWindow(edit)); Assert.True(FreeLibrary(library));
        }
    });

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint handle);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowText(nint handle, string text);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(nint module);
}
