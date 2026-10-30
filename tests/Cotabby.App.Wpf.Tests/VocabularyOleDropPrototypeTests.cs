using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Cotabby.Win32.Vocabulary;
using System.Runtime.InteropServices.ComTypes;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class VocabularyOleDropPrototypeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ClosingAfterReadyBeforePublicationRetiresReturnedReceiver() => dispatcher.RunAsync(async () =>
    {
        var ready = new TaskCompletionSource<(WindowsVocabularyOleDropTransfer Receiver, Process Owner)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<WindowsVocabularyOleDropTransfer> Start(IntPtr parent, CancellationToken token)
        { var receiver = await WindowsVocabularyOleDropTransfer.StartForTestAsync(parent, token); ready.TrySetResult((receiver, CaptureOwner(receiver))); await release.Task; return receiver; }
        var surface = new Cotabby.App.UI.Vocabulary.CanonicalImageDropSurface(_ => { }, () => { }, _ => { }, Start);
        var window = new Window { Content = surface, Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        try
        {
            var pair = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); var receiver = pair.Receiver; using var owner = pair.Owner;
            Task retirement = surface.RetireAsync(); Assert.False(retirement.IsCompleted); release.SetResult();
            await retirement.WaitAsync(TimeSpan.FromSeconds(5)); await AssertRetired(receiver, owner);
        }
        finally { release.TrySetResult(); await surface.RetireAsync(); window.Close(); }
    });
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PresentationFailureRetiresOldReceiverBeforeRetry(bool positionFailure) => dispatcher.RunAsync(async () =>
    {
        var first = new TaskCompletionSource<(WindowsVocabularyOleDropTransfer Receiver, Process Owner)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<(WindowsVocabularyOleDropTransfer Receiver, Process Owner)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0, throws = 0;
        async Task<WindowsVocabularyOleDropTransfer> Start(IntPtr parent, CancellationToken token)
        {
            var receiver = await WindowsVocabularyOleDropTransfer.StartForTestAsync(parent, token);
            var pair = (receiver, CaptureOwner(receiver));
            if (Interlocked.Increment(ref starts) == 1) first.TrySetResult(pair); else if (!second.TrySetResult(pair)) pair.Item2.Dispose();
            return receiver;
        }
        void Busy(bool busy) { if (!positionFailure && !busy && Interlocked.Increment(ref throws) <= 2) throw new InvalidOperationException("synthetic busy callback"); }
        var surface = new Cotabby.App.UI.Vocabulary.CanonicalImageDropSurface(_ => { }, () => { }, Busy, Start);
        if (positionFailure) surface.BeforeInitialPosition = () => { if (Interlocked.Increment(ref throws) == 1) throw new InvalidOperationException("synthetic position failure"); };
        var window = new Window { Content = surface, Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        try
        {
            var firstPair = await first.Task.WaitAsync(TimeSpan.FromSeconds(10)); var original = firstPair.Receiver; using var originalOwner = firstPair.Owner;
            if (!positionFailure) await Task.Run(() => InvokeDrop(Convert.FromBase64String(original.TestTarget!), new PngData())).WaitAsync(TimeSpan.FromSeconds(10));
            var nextPair = await second.Task.WaitAsync(TimeSpan.FromSeconds(10)); var next = nextPair.Receiver; using var nextOwner = nextPair.Owner;
            await AssertRetired(original, originalOwner); Assert.True(IsWindow(next.Window));
            await surface.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5)); await AssertRetired(next, nextOwner);
        }
        finally { await surface.RetireAsync(); window.Close(); }
    });
    [Fact]
    public Task HelperOwnsItsRegisteredChildWindow() => dispatcher.RunAsync(async () =>
    {
        var window = new Window { Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        IntPtr parent = new WindowInteropHelper(window).Handle;
        var start = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--ole-drop"); start.ArgumentList.Add(parent.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add("Drop image here");
        using var process = Process.Start(start)!;
        try
        {
            await process.StandardInput.WriteLineAsync("GO"); await process.StandardInput.FlushAsync();
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (line is null) throw new InvalidOperationException("Helper exited: " + await process.StandardError.ReadToEndAsync());
            Assert.StartsWith("READY ", line); IntPtr child = new(long.Parse(line[6..], System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(IsWindow(child)); Assert.Equal(parent, GetParent(child)); GetWindowThreadProcessId(child, out uint pid); Assert.Equal((uint)process.Id, pid);
        }
        finally
        {
            if (!process.HasExited) process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); window.Close();
        }
    });
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public Task BorrowedJobPositionsAndRetiresTheHelperWindow(double scale) => dispatcher.RunAsync(async () =>
    {
        var window = new Window { Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        IntPtr parent = new WindowInteropHelper(window).Handle;
        var transfer = await WindowsVocabularyOleDropTransfer.StartAsync(parent, CancellationToken.None);
        IntPtr child = transfer.Window;
        using var owner = CaptureOwner(transfer);
        try
        {
            Assert.True(IsWindow(child)); int width = (int)(100 * scale), height = (int)(40 * scale);
            transfer.Position(10, 10, width, height, true); var until = DateTime.UtcNow.AddSeconds(5);
            while (!GetWindowRect(child, out var rectangle) || rectangle.Right - rectangle.Left != width || rectangle.Bottom - rectangle.Top != height)
            { Assert.True(DateTime.UtcNow < until); await Task.Delay(10); }
            Assert.False(transfer.Result.IsCompleted);
        }
        finally { await transfer.DisposeAsync(); window.Close(); }
        await AssertRetired(transfer, owner);
    });
    private static Process CaptureOwner(WindowsVocabularyOleDropTransfer receiver)
    {
        Assert.True(IsWindow(receiver.Window)); Assert.NotEqual(0u, GetWindowThreadProcessId(receiver.Window, out uint pid)); Assert.Equal((uint)receiver.ProcessId, pid);
        var process = Process.GetProcessById((int)pid); _ = process.Handle; return process;
    }
    private static async Task AssertRetired(WindowsVocabularyOleDropTransfer receiver, Process owner)
    {
        int pid = owner.Id; await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(owner.HasExited); Assert.True(receiver.Result.IsCompleted);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (IsWindow(receiver.Window) && GetWindowThreadProcessId(receiver.Window, out uint current) != 0 && current == pid)
        { Assert.True(DateTime.UtcNow < deadline, $"Exited helper PID {pid} still owns HWND {receiver.Window}."); await Task.Delay(10); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SyntheticProviderIsReadByTheHelperAcrossProcesses(bool bitmap) => dispatcher.RunAsync(async () =>
    {
        var window = new Window { Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        var start = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--ole-drop-test"); start.ArgumentList.Add(new WindowInteropHelper(window).Handle.ToInt64().ToString());
        using var process = Process.Start(start)!;
        try
        {
            await process.StandardInput.WriteLineAsync("GO"); await process.StandardInput.FlushAsync();
            var stdout = process.StandardOutput.BaseStream;
            Assert.StartsWith("READY ", await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10)));
            string? target = await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10)); Assert.StartsWith("TARGET ", target);
            byte[] wire = Convert.FromBase64String(target![7..]); var provider = new PngData { Gdi = bitmap };
            var call = Task.Run(() => InvokeDrop(wire, provider));
            Assert.Equal("DROP", await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10)));
            var reading = VocabularyImageTransferProtocol.ReadAsync(stdout, CancellationToken.None);
            if (await Task.WhenAny(reading, Task.Delay(5000)) != reading) throw new InvalidOperationException($"Read held; call={call.Status}, reads={provider.Reads}, exited={process.HasExited}");
            byte[] bytes;
            try { bytes = (await reading)!; }
            catch (Exception error)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                throw new InvalidDataException($"Synthetic bitmap={bitmap}; reads={provider.Reads}; child={process.ExitCode}; diagnostic={await process.StandardError.ReadToEndAsync()}", error);
            }
            if (bitmap) Assert.Equal(Cotabby.Tests.Shared.VocabularyBitmapFixture.Pixels, bytes[54..]);
            else Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
            await call.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, provider.Reads);
        }
        finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); window.Close(); }
    });
    [Fact]
    public Task HeldCrossProcessProviderCannotPreventJobRetirement() => dispatcher.RunAsync(async () =>
    {
        var window = new Window { Width = 300, Height = 200, ShowInTaskbar = false }; window.Show();
        var start = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "Cotabby.ImageTransferHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--ole-drop-test"); start.ArgumentList.Add(new WindowInteropHelper(window).Handle.ToInt64().ToString());
        using var job = new WindowsVocabularyDropJob(); using var process = Process.Start(start)!; using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task? call = null;
        try
        {
            Assert.True(AssignProcessToJobObject(job.Handle, process.Handle));
            await process.StandardInput.WriteLineAsync("GO"); await process.StandardInput.FlushAsync(); var stdout = process.StandardOutput.BaseStream;
            Assert.StartsWith("READY ", await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10)));
            string target = await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10));
            var provider = new PngData { Entered = entered, Release = release }; call = Task.Run(() => InvokeDrop(Convert.FromBase64String(target[7..]), provider));
            Assert.Equal("DROP", await ReadLine(stdout).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10)))); Assert.False(call.IsCompleted);
            job.Dispose(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(process.HasExited);
        }
        finally
        {
            release.Set(); if (!process.HasExited) process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (call is not null) { try { await call.WaitAsync(TimeSpan.FromSeconds(5)); } catch { } } window.Close();
        }
    });
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task CrossProcessDropSavesThroughCanonicalEditorAndNamedStore(int kind) => dispatcher.RunAsync(async () =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "ole-canonical-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] png = Convert.FromBase64String(fixture.RootElement.GetProperty("input").GetString()!);
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(folder, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new(Expression: "image")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
            var started = new TaskCompletionSource<WindowsVocabularyOleDropTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WindowsVocabularyOleDropTransfer> Start(IntPtr hwnd, CancellationToken token) { var receiver = await WindowsVocabularyOleDropTransfer.StartForTestAsync(hwnd, token); started.TrySetResult(receiver); return receiver; }
            var editor = new Cotabby.App.UI.Vocabulary.FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, dropStart: Start); editor.Show();
            try
            {
                var receiver = await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); var provider = new PngData(png) { Virtual = kind is 1 or 2, Stream = kind == 2, Gdi = kind == 3 };
                if (kind == 0) CaptureNative(editor);
                await Task.Run(() => InvokeDrop(Convert.FromBase64String(receiver.TestTarget!), provider)).WaitAsync(TimeSpan.FromSeconds(10));
                var until = DateTime.UtcNow.AddSeconds(10);
                while (editor.Draft.Senses.Single().Content?.Count != 1) { Assert.True(DateTime.UtcNow < until); await Task.Delay(20); }
                var save = FindButton(editor, "canonicalEditSave"); while (!save.IsEnabled) { Assert.True(DateTime.UtcNow < until); await Task.Delay(20); }
                save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await editor.LastOperation!; await editor.RetireAsync();
                var image = Assert.IsType<Cotabby.Core.Vocabulary.VocabularyBlockContent.Image>((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content!.Single().Content);
                Assert.NotEmpty(await store.ReadImageAsync(image.Reference));
                if (kind == 3) { Assert.Equal(3, image.Reference.Width); Assert.Equal(2, image.Reference.Height); }
            }
            finally { await editor.RetireAsync(); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });
    private static void CaptureNative(Window window)
    {
        string? directory = Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR"); if (string.IsNullOrEmpty(directory)) return;
        IntPtr hwnd = new WindowInteropHelper(window).Handle; if (!GetWindowRect(hwnd, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top; IntPtr dc = GetDC(hwnd), memory = CreateCompatibleDC(dc), bitmap = CreateCompatibleBitmap(dc, width, height);
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, 2)) return;
            SelectObject(memory, old);
            var info = new BitmapInfo { Header = new BitmapHeader { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 } };
            byte[] pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) return;
            var image = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            Directory.CreateDirectory(directory); using var file = File.Create(Path.Combine(directory, "canonical-ole-drop.png")); encoder.Save(file);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(hwnd, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    { internal uint Size; internal int Width, Height; internal ushort Planes, Bits; internal uint Compression, ImageSize; internal int XPels, YPels; internal uint Colors, Important; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal BitmapHeader Header; internal uint Colors; }
    [Fact]
    public Task ClosingEditorKillsHeldProviderBeforeTheProviderReturns() => dispatcher.RunAsync(async () =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "ole-close-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); Task? call = null;
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(folder, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new(Expression: "image")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
            var started = new TaskCompletionSource<WindowsVocabularyOleDropTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WindowsVocabularyOleDropTransfer> Start(IntPtr hwnd, CancellationToken token) { var receiver = await WindowsVocabularyOleDropTransfer.StartForTestAsync(hwnd, token); started.TrySetResult(receiver); return receiver; }
            var editor = new Cotabby.App.UI.Vocabulary.FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, dropStart: Start); editor.Show();
            try
            {
                var receiver = await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); var provider = new PngData { Entered = entered, Release = release };
                call = Task.Run(() => InvokeDrop(Convert.FromBase64String(receiver.TestTarget!), provider));
                Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10)))); Assert.False(call.IsCompleted);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (FindButton(editor, "canonicalEditSave").IsEnabled) { Assert.True(DateTime.UtcNow < deadline); await Task.Delay(10); }
                editor.Close(); await editor.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(receiver.Result.IsCompleted);
                Assert.False(editor.IsVisible); Assert.Empty((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content ?? []);
            }
            finally { release.Set(); await editor.RetireAsync(); if (call is not null) { try { await call.WaitAsync(TimeSpan.FromSeconds(5)); } catch { } } }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });
    [Fact]
    public Task InvalidDropShowsErrorAndDoesNotCommit() => dispatcher.RunAsync(async () =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "ole-error-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(folder, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new(Expression: "image")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
            var started = new TaskCompletionSource<WindowsVocabularyOleDropTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WindowsVocabularyOleDropTransfer> Start(IntPtr hwnd, CancellationToken token) { var receiver = await WindowsVocabularyOleDropTransfer.StartForTestAsync(hwnd, token); started.TrySetResult(receiver); return receiver; }
            var editor = new Cotabby.App.UI.Vocabulary.FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id, dropStart: Start); editor.Show();
            try
            {
                var receiver = await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Task.Run(() => InvokeDrop(Convert.FromBase64String(receiver.TestTarget!), new PngData())).WaitAsync(TimeSpan.FromSeconds(10));
                var until = DateTime.UtcNow.AddSeconds(10);
                while (!HasText(editor, "Unable to add this image. Try a smaller image.")) { Assert.True(DateTime.UtcNow < until); await Task.Delay(20); }
                Assert.Empty((await store.ReadAsync()).Document.Entries.Single().Senses.Single().Content ?? []);
            }
            finally { await editor.RetireAsync(); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });
    private static bool HasText(DependencyObject node, string value)
    {
        if (node is System.Windows.Controls.TextBlock text && text.Text == value) return true;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) if (HasText(System.Windows.Media.VisualTreeHelper.GetChild(node, i), value)) return true;
        return false;
    }
    private static System.Windows.Controls.Button FindButton(DependencyObject parent, string name)
    {
        if (parent is System.Windows.Controls.Button button && button.Name == name) return button;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            try { return FindButton(System.Windows.Media.VisualTreeHelper.GetChild(parent, i), name); } catch (InvalidOperationException) { }
        throw new InvalidOperationException("Button not found.");
    }
    private static async Task<string> ReadLine(Stream stream)
    {
        using var bytes = new MemoryStream(); byte[] next = new byte[1];
        while (bytes.Length < 65536) { await stream.ReadExactlyAsync(next); if (next[0] == '\n') return System.Text.Encoding.ASCII.GetString(bytes.ToArray()); bytes.WriteByte(next[0]); }
        throw new InvalidDataException();
    }
    private static void InvokeDrop(byte[] wire, PngData provider)
    {
        IntPtr memory = GlobalAlloc(2, (nuint)wire.Length), data = GlobalLock(memory); Marshal.Copy(wire, 0, data, wire.Length); GlobalUnlock(memory);
        Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(memory, true, out IntPtr stream)); IntPtr target = IntPtr.Zero;
        try
        {
            Guid iid = new("00000122-0000-0000-C000-000000000046"); Marshal.ThrowExceptionForHR(CoUnmarshalInterface(stream, ref iid, out target));
            var drop = (IDropTarget)Marshal.GetObjectForIUnknown(target);
            try { uint effect = 1; Marshal.ThrowExceptionForHR(drop.Drop(provider, 0, default, ref effect)); Assert.Equal(1u, effect); }
            finally { Marshal.ReleaseComObject(drop); }
        }
        finally { if (target != IntPtr.Zero) Marshal.Release(target); Marshal.Release(stream); }
    }
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, Point point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, Point point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, Point point, ref uint effect);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class PngData(byte[]? supplied = null) : IDataObject
    {
        public int Reads;
        public bool Virtual, Stream, Gdi;
        public ManualResetEventSlim? Entered, Release;
        public int QueryGetData(ref FORMATETC format)
        {
            uint id = unchecked((ushort)format.cfFormat);
            if (Gdi) return id == 2 && format.tymed == TYMED.TYMED_GDI ? 0 : unchecked((int)0x80040064);
            if (Virtual)
                return id == RegisterClipboardFormatW("FileGroupDescriptorW") || id == RegisterClipboardFormatW("FileContents") ? 0 : unchecked((int)0x80040064);
            if (id != RegisterClipboardFormatW("PNG")) return unchecked((int)0x80040064);
            Entered?.Set(); Release?.Wait(TimeSpan.FromSeconds(20)); return 0;
        }
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (Gdi)
            {
                Interlocked.Increment(ref Reads); using var bitmap = new Cotabby.Tests.Shared.VocabularyBitmapFixture(transferable: true);
                medium = new() { tymed = TYMED.TYMED_GDI, unionmember = bitmap.Transfer() }; return;
            }
            Interlocked.Increment(ref Reads); byte[] bytes = supplied ?? [1, 2, 3];
            bool descriptor = Virtual && unchecked((ushort)format.cfFormat) == RegisterClipboardFormatW("FileGroupDescriptorW");
            if (descriptor) { bytes = new byte[596]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); System.Text.Encoding.Unicode.GetBytes("image.png\0").CopyTo(bytes, 76); }
            IntPtr memory = GlobalAlloc(2, (nuint)bytes.Length), data = GlobalLock(memory); Marshal.Copy(bytes, 0, data, bytes.Length); GlobalUnlock(memory);
            if (Virtual && Stream && !descriptor) { Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(memory, true, out IntPtr stream)); medium = new() { tymed = TYMED.TYMED_ISTREAM, unionmember = stream }; }
            else medium = new() { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        }
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = default; return 1; }
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotImplementedException();
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotImplementedException();
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return 1; }
        public void DUnadvise(int connection) => throw new NotImplementedException();
        public int EnumDAdvise(out IEnumSTATDATA? value) { value = null; return 1; }
    }
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeHandle job, IntPtr process);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IntPtr stream);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoUnmarshalInterface(IntPtr stream, ref Guid iid, out IntPtr result);
}
