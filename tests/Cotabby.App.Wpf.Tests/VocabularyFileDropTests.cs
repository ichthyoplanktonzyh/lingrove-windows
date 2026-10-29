using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Real HDROP allocations and SendMessage through the actual WPF HWND hook. No OLE provider or
/// runner clipboard is touched. This proves message/layout/ownership and editor handoff, not
/// physical Explorer drag interoperability, ANSI paths, virtual files or pixel-only providers.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyFileDropTests(WpfDispatcher dispatcher)
{
    private const int DropMessage = 0x0233;
    private static readonly byte[] Png = Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-image-storage-v1.json"))).RootElement.GetProperty("input").GetString()!);
    private static byte[] Payload(string[] paths, Point point)
    {
        byte[] list = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0"); byte[] bytes = new byte[20 + list.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 20); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)point.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), (int)point.Y); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1); list.CopyTo(bytes, 20); return bytes;
    }
    private static IntPtr Allocation(byte[] bytes)
    {
        IntPtr memory = GlobalAlloc(0x42, (nuint)bytes.Length); Assert.NotEqual(IntPtr.Zero, memory);
        IntPtr pointer = GlobalLock(memory); Assert.NotEqual(IntPtr.Zero, pointer);
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { GlobalUnlock(memory); }
        return memory;
    }
    private static HwndSource Source(Window window) => Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));
    private static Point DevicePoint(Window window, FrameworkElement element)
    {
        var source = Source(window); var root = Assert.IsAssignableFrom<Visual>(source.RootVisual);
        Point dip = element.TransformToAncestor(root).Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return source.CompositionTarget.TransformToDevice.Transform(dip);
    }
    private static void Drop(Window window, byte[] bytes) => SendMessageW(Source(window).Handle, DropMessage, Allocation(bytes), IntPtr.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public Task ReceiverDetachesOnlyTheFirstUnicodePathAndFreesShellMemoryBeforeCallback(int unicodeFlag) => dispatcher.Run(() =>
    {
        var window = new Window { Content = new Border(), Width = 300, Height = 150 }; window.Show(); var source = Source(window);
        string first = Path.GetFullPath("first.png"), second = Path.GetFullPath("second.png"); var point = new Point(23, 41);
        byte[] bytes = Payload([first, second], point); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), unicodeFlag);
        IntPtr memory = Allocation(bytes); string? received = null; Point position = default;
        using var receiver = new WindowsVocabularyFileDrop(source, (path, client) =>
        {
            Assert.Equal(0x8000u, GlobalFlags(memory)); received = path; position = client;
        });
        try
        {
            Assert.NotEqual(0L, GetWindowLongPtrW(source.Handle, -20).ToInt64() & 0x10);
            SendMessageW(source.Handle, DropMessage, memory, IntPtr.Zero); Assert.Equal(first, received); Assert.Equal(point, position);
            receiver.Dispose(); Assert.Equal(0L, GetWindowLongPtrW(source.Handle, -20).ToInt64() & 0x10);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("short")]
    [InlineData("oversized")]
    [InlineData("offset")]
    [InlineData("unaligned")]
    [InlineData("ansi")]
    [InlineData("non-client")]
    [InlineData("relative")]
    [InlineData("empty")]
    [InlineData("unterminated")]
    [InlineData("surrogate")]
    public Task MalformedOrUnsupportedPayloadsAreFreedWithoutPublishingAPath(string mode) => dispatcher.Run(() =>
    {
        var window = new Window { Content = new Border(), Width = 200, Height = 150 }; window.Show(); var source = Source(window); int calls = 0;
        using var receiver = new WindowsVocabularyFileDrop(source, (_, _) => ++calls);
        byte[] bytes = Payload([Path.GetFullPath("selected.png")], new(1, 2));
        switch (mode)
        {
            case "short": bytes = new byte[8]; break;
            case "oversized": bytes = new byte[1024 * 1024 + 1]; break;
            case "offset": BinaryPrimitives.WriteInt32LittleEndian(bytes, int.MaxValue); break;
            case "unaligned": BinaryPrimitives.WriteInt32LittleEndian(bytes, 21); break;
            case "ansi": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0); break;
            case "non-client": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1); break;
            case "relative": bytes = Payload(["relative.png"], new()); break;
            case "empty": bytes = Payload([""], new()); break;
            case "unterminated": Array.Fill(bytes, (byte)65, 20, bytes.Length - 20); break;
            case "surrogate": bytes[20] = 0; bytes[21] = 0xD8; break;
        }
        IntPtr memory = Allocation(bytes);
        try { SendMessageW(source.Handle, DropMessage, memory, IntPtr.Zero); Assert.Equal(0, calls); Assert.Equal(0x8000u, GlobalFlags(memory)); }
        finally { window.Close(); }
    });

    [Fact]
    public Task CanonicalEditorAcceptsDropOnlyInsideExplanation() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); string file = Path.Combine(folder.Path, "image.png"); File.WriteAllBytes(file, Png);
        await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(folder.Path, FrozenVocabularyStoreFactory.CanonicalFileName), false);
        await store.AddEntriesAsync([new(Expression: "hello")]); var entry = (await store.ReadAsync()).Document.Entries.Single();
        var editor = new FrozenVocabularyEntryEditorWindow(store, store, entry, entry.Senses.Single().Id); editor.Show();
        try
        {
            editor.UpdateLayout(); var expression = FindVisual<TextBox>(editor).Single(t => t.Name == "canonicalEditExpression");
            Drop(editor, Payload([file], DevicePoint(editor, expression))); Assert.Null(editor.Draft.Senses.Single().Content);
            var area = FindVisual<StackPanel>(editor).Single(p => p.Name == "canonicalExplanationDropArea"); area.BringIntoView(); editor.UpdateLayout();
            var add = FindVisual<Button>(area).Single(b => Equals(b.Content, "Add image")); add.BringIntoView(); editor.UpdateLayout();
            Drop(editor, Payload([file], DevicePoint(editor, add)));
            for (int i = 0; i < 200 && editor.Draft.Senses.Single().Content is null; i++) await Task.Delay(10);
            Assert.Single(editor.Draft.Senses.Single().Content!);
        }
        finally { editor.Close(); await editor.RetireAsync(); }
    });
    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T match) yield return match; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in FindVisual<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EditorHandsOffOnlyTheFirstPathAndNeverFallsBackAfterItsDecodeFails(bool firstInvalid) => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var editor = Editor(store);
        try
        {
            string first = Path.Combine(folder.Path, "first.png"), second = Path.Combine(folder.Path, "second.png");
            File.WriteAllBytes(first, firstInvalid ? [1, 2, 3] : Png); File.WriteAllBytes(second, Png);
            var area = Assert.IsType<Border>(editor.FindName("ExplanationDropArea")); area.BringIntoView(); editor.UpdateLayout();
            Drop(editor, Payload([first, second], DevicePoint(editor, area))); await editor.LastImport;
            if (firstInvalid) { Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount); Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(editor.FindName("ImportFailure")).Visibility); }
            else { Assert.Single(editor.Draft.Senses[0].Content!); Assert.Equal(1, editor.ImportedImageCount); }
            Assert.Equal(1UL, store.Snapshot.Revision);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task FooterAndFieldsOutsideTheClippedExplanationAreaCannotStartAnImport() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); string file = Path.Combine(folder.Path, "image.png"); File.WriteAllBytes(file, Png);
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive); await store.SaveAsync(Capture()); var baseline = store.Entries[0];
        store.Apply(await archive.CommitAsync(store.Snapshot.Document with { Entries = [baseline with { Senses = [baseline.Senses[0] with
        { Content = Enumerable.Range(0, 20).Select(_ => VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("text")))).ToArray() }] }] }));
        var editor = Editor(store);
        try
        {
            var original = editor.LastImport;
            foreach (string name in new[] { "ExpressionText", "SaveButton" })
            {
                var field = Assert.IsAssignableFrom<FrameworkElement>(editor.FindName(name));
                Drop(editor, Payload([file], DevicePoint(editor, field))); Assert.Same(original, editor.LastImport);
            }
            var source = Source(editor); var area = Assert.IsType<Border>(editor.FindName("ExplanationDropArea"));
            Point footer = DevicePoint(editor, Assert.IsType<Button>(editor.FindName("SaveButton")));
            Assert.False(VocabularyEntryEditorWindow.ContainsDropPoint(area, (Visual)source.RootVisual, footer, source.CompositionTarget.TransformFromDevice));
            Assert.Equal(0, editor.ImportedImageCount); Assert.Equal(20, editor.Draft.Senses[0].Content!.Count);
        }
        finally { await editor.RetireAsync(); }
    });
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public Task ClientPixelsUseTheSuppliedDpiTransformBeforeActualVisualHitTesting(double scale) => dispatcher.Run(() =>
    {
        var area = new Border { Margin = new Thickness(30, 20, 50, 40), Background = Brushes.Transparent }; var grid = new Grid(); grid.Children.Add(area);
        var window = new Window { Content = grid, Width = 400, Height = 250 }; window.Show(); window.UpdateLayout();
        try
        {
            var root = (Visual)Source(window).RootVisual;
            Point inside = area.TransformToAncestor(root).Transform(new Point(area.ActualWidth / 2, area.ActualHeight / 2));
            var toPixels = new Matrix(scale, 0, 0, scale, 0, 0); var toDips = new Matrix(1 / scale, 0, 0, 1 / scale, 0, 0);
            Assert.True(VocabularyEntryEditorWindow.ContainsDropPoint(area, root, toPixels.Transform(inside), toDips));
            Assert.False(VocabularyEntryEditorWindow.ContainsDropPoint(area, root, toPixels.Transform(new Point(1, 1)), toDips));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task AHiddenOrDisabledEditorCannotImportAnInjectedDrop() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); string file = Path.Combine(folder.Path, "image.png"); File.WriteAllBytes(file, Png);
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var editor = Editor(store);
        try
        {
            var area = Assert.IsType<Border>(editor.FindName("ExplanationDropArea")); area.BringIntoView(); editor.UpdateLayout(); var point = DevicePoint(editor, area); var original = editor.LastImport;
            editor.IsEnabled = false; Drop(editor, Payload([file], point)); Assert.Same(original, editor.LastImport);
            editor.IsEnabled = true; editor.Hide(); Drop(editor, Payload([file], point)); Assert.Same(original, editor.LastImport);
            Assert.Equal(0, editor.ImportedImageCount); Assert.Null(editor.Draft.Senses[0].Content);
        }
        finally { await editor.RetireAsync(); }
    });
    [Fact]
    public Task CallbackFailureCannotLeakShellMemoryOrEscapeTheWindowProcedure() => dispatcher.Run(() =>
    {
        var window = new Window { Content = new Border(), Width = 200, Height = 150 }; window.Show();
        IntPtr memory = Allocation(Payload([Path.GetFullPath("image.png")], new(10, 10))); int callbacks = 0; bool freedBeforeCallback = false;
        using var receiver = new WindowsVocabularyFileDrop(Source(window), (_, _) =>
        { ++callbacks; freedBeforeCallback = GlobalFlags(memory) == 0x8000u; throw new InvalidOperationException("test presentation failure"); });
        try { SendMessageW(Source(window).Handle, DropMessage, memory, IntPtr.Zero); Assert.Equal(1, callbacks); Assert.True(freedBeforeCallback); }
        finally { window.Close(); }
    });
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static VocabularyCapture Capture() => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "meaning");
    private static VocabularyEntryEditorWindow Editor(VocabularyStore store)
    { var entry = store.Entries[0]; var editor = new VocabularyEntryEditorWindow(store, entry, entry.Senses[0].Id); editor.Show(); editor.UpdateLayout(); return editor; }
    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-file-drop-" + Guid.NewGuid().ToString("N"));
        internal Folder() => VocabularyWindowsFile.CreateParentDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr SendMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GlobalFlags(IntPtr memory);
}
