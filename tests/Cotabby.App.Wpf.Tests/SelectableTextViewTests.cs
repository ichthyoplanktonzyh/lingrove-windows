using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Learning;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Runs the literal-selection adapter in an actual WPF window without a clipboard writer.</summary>
[Collection("WPF views")]
public sealed class SelectableTextViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public async Task SelectionLayoutSettlesAndAllowsDispatcherIdleToRun()
    {
        Window? window = null;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await dispatcher.Run(() =>
            {
                var shown = Show("Selection must not sustain a render and layout loop.");
                window = shown.Window;
                shown.Text.Select(0, shown.Text.Text.Length);
                window.Dispatcher.BeginInvoke(() => idle.TrySetResult(), DispatcherPriority.ApplicationIdle);
            });
            await idle.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if (window is not null) await dispatcher.Run(window.Close); }
    }

    [Fact]
    public Task SelectionAcrossStyledRunsCopiesLiteralTextWithoutChangingRunsOrTakingKeyboardFocus() => dispatcher.Run(() =>
    {
        var (window, text) = Show("unused");
        try
        {
            text.UpdateLiteralContent(() =>
            {
                text.Inlines.Clear();
                text.Inlines.Add(new Run("  cafe\u0301 ") { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new Run("<b>العربية</b> 👩🏽‍💻\n"));
            });
            window.UpdateLayout();
            var runs = text.Inlines.ToArray();
            var focus = Keyboard.FocusedElement;
            text.Select(2, text.Text.Length);
            SelectedTextCopyLease? emitted = null;
            window.AddHandler(NonactivatingSelectableText.CopyRequestedEvent,
                new EventHandler<SelectedTextCopyEventArgs>((_, args) => emitted = args.Lease));
            var lease = text.RequestCopy();
            Assert.Same(lease, emitted); Assert.NotNull(lease);
            Assert.Equal("cafe\u0301 <b>العربية</b> 👩🏽‍💻\n", lease.Text);
            Assert.Equal(text.DocumentIdentity, lease.DocumentIdentity); Assert.True(lease.IsCurrent);
            Assert.Equal(runs, text.Inlines.ToArray());
            Assert.Same(focus, Keyboard.FocusedElement); Assert.False(text.Focusable);
            text.Focusable = true; Assert.False(text.Focusable);
            AssertNonactivating(window); AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RunsCreatedBeforeFirstMeasureSupportSelectionAndSelectAllWithoutWarmingTheTextProperty() => dispatcher.Run(() =>
    {
        var text = new NonactivatingSelectableText { DocumentIdentity = Guid.NewGuid() };
        text.UpdateLiteralContent(() =>
        {
            text.Inlines.Add(new Run("keep ") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run("me posted"));
        });
        var window = new Window { ShowActivated = false, Focusable = false, Content = text };
        try
        {
            window.Show(); window.UpdateLayout();
            text.Select(5, 7);
            var lease = text.RequestCopy()!;
            Assert.NotNull(lease); Assert.Equal("me", lease.Text);
            text.OpenCopyMenu();
            var buttons = Assert.IsType<StackPanel>(Assert.IsType<Border>(text.CopyMenu!.Child).Child).Children.OfType<Button>().ToArray();
            Click(buttons[1]);
            Assert.Equal("keep me posted", text.Selection!.Text); Assert.False(lease.IsCurrent);
            var all = text.RequestCopy()!;
            Assert.Equal("keep me posted", all.Text);
            text.Inlines.Add(new Run("!"));
            Assert.False(all.IsCurrent); Assert.Null(text.Selection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EqualLiteralRebuildRetainsSelectionButChangedRawUnicodeRevokesIt() => dispatcher.Run(() =>
    {
        var (window, text) = Show("cafe\u0301");
        try
        {
            text.Select(0, text.Text.Length);
            var lease = text.RequestCopy()!;
            text.UpdateLiteralContent(() => { text.Inlines.Clear(); text.Inlines.Add(new Run("cafe\u0301")); });
            Assert.Equal("cafe\u0301", text.Selection!.Text); Assert.True(lease.IsCurrent);
            text.Text = "café";
            Assert.Null(text.Selection); Assert.False(lease.IsCurrent);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DocumentRetargetAndHideRevokeLeasesEvenWhenTextDoesNotChange() => dispatcher.Run(() =>
    {
        var (window, text) = Show("same text");
        try
        {
            text.Select(0, 4); var first = text.RequestCopy()!;
            Guid sameTarget = Assert.IsType<Guid>(text.DocumentIdentity);
            text.DocumentIdentity = sameTarget; // Equal identity in a newly allocated object box.
            Assert.True(first.IsCurrent); Assert.Equal("same", text.Selection!.Text);
            text.DocumentIdentity = Guid.NewGuid();
            Assert.False(first.IsCurrent); Assert.Null(text.Selection);
            text.Select(0, 4); var second = text.RequestCopy()!;
            text.OpenCopyMenu();
            var menu = text.CopyMenu!;
            window.Hide();
            Assert.False(second.IsCurrent); Assert.Null(text.Selection); Assert.False(menu.IsOpen);
            Assert.Null(text.RequestCopy());
            window.Show(); window.UpdateLayout();
            Assert.Null(text.Selection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PlainPopupUsesNoActivationStyleAndEmitsOnlyItsCurrentSelectionReceipt() => dispatcher.Run(() =>
    {
        var (window, text) = Show("copy this literal text");
        try
        {
            text.Select(0, 4);
            var focus = Keyboard.FocusedElement;
            text.OpenCopyMenu();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var menu = text.CopyMenu!;
            Assert.True(menu.IsOpen); Assert.False(menu.Focusable);
            var border = Assert.IsType<Border>(menu.Child);
            var buttons = Assert.IsType<StackPanel>(border.Child).Children.OfType<Button>().ToArray();
            Assert.Equal(new[] { "Copy", "Select all" }, buttons.Select(button => button.Content));
            Assert.All(buttons, button => Assert.False(button.Focusable));
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(border));
            Assert.Equal(0x08000080, GetWindowLong(source.Handle, -20) & 0x08000080);
            Assert.Same(focus, Keyboard.FocusedElement);
            SelectedTextCopyLease? copied = null;
            text.CopyRequested += (_, args) => copied = args.Lease;
            Click(buttons[0]);
            Assert.Equal("copy", copied!.Text); Assert.False(menu.IsOpen);
            text.DocumentIdentity = Guid.NewGuid();
            text.Select(5, 9);
            Click(buttons[0]); // Removed/closed menu cannot copy a later target.
            Assert.Equal("copy", copied.Text); Assert.False(copied.IsCurrent);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task WrappedBidiAndEmojiSelectionPaintsBoundedCellsOnMultipleLines() => dispatcher.Run(() =>
    {
        var (window, text) = Show("alpha 👩🏽‍💻 cafe\u0301 العربية beta gamma delta epsilon.\nSecond literal line.", 180);
        try
        {
            text.Select(0, text.Text.Length);
            var cells = text.SelectionRectangles();
            Assert.NotEmpty(cells);
            Assert.True(cells.Select(cell => Math.Round(cell.Y)).Distinct().Count() > 1);
            Assert.All(cells, cell =>
            {
                Assert.True(double.IsFinite(cell.X) && double.IsFinite(cell.Y));
                Assert.True(cell.Width > 0 && cell.Height > 0);
                Assert.True(cell.Left >= 0 && cell.Right <= text.ActualWidth + 0.5);
                Assert.True(cell.Top >= 0 && cell.Bottom <= text.ActualHeight + 0.5);
            });
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmbeddedLinkCannotSilentlyBecomeASelectablePlainTextPayload() => dispatcher.Run(() =>
    {
        var (window, text) = Show("literal");
        try
        {
            text.UpdateLiteralContent(() => { text.Inlines.Clear(); text.Inlines.Add(new Hyperlink(new Run("link"))); });
            text.Select(0, text.Text.Length);
            Assert.Null(text.Selection); Assert.Null(text.RequestCopy());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ActualTextPointerHitTestingMapsAcrossRunMarkupToTheRawUtf16Offsets() => dispatcher.Run(() =>
    {
        var (window, text) = Show("alpha beta");
        try
        {
            text.UpdateLiteralContent(() => { text.Inlines.Clear(); text.Inlines.Add(new Run("alpha ")); text.Inlines.Add(new Run("beta")); });
            window.UpdateLayout();
            Rect first = text.PointerAt(0)!.GetCharacterRect(LogicalDirection.Forward);
            Rect end = text.PointerAt(6)!.GetCharacterRect(LogicalDirection.Forward);
            Assert.True(text.BeginSelection(new Point(first.Left + 0.1, first.Top + first.Height / 2)));
            text.ExtendSelection(new Point(end.Left + 0.1, end.Top + end.Height / 2));
            Assert.Equal("alpha ", text.Selection!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ScrolledLongDocumentClipsSelectionToTheActualVisibleViewport() => dispatcher.Run(() =>
    {
        string value = string.Join("\n", Enumerable.Range(0, 500).Select(index => $"Line {index}: alpha العربية 👩🏽‍💻."));
        var (window, text) = Show(value);
        try
        {
            var border = Assert.IsType<Border>(window.Content);
            border.Child = null;
            var scroll = new ScrollViewer { Content = text, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            border.Child = scroll;
            window.UpdateLayout(); scroll.ScrollToVerticalOffset(1500); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            text.Select(0, text.Text.Length);
            var cells = text.SelectionRectangles();
            Assert.NotEmpty(cells); Assert.InRange(cells.Count, 1, 100);
            Assert.All(cells, cell =>
            {
                Assert.True(cell.Top >= scroll.VerticalOffset - 0.5);
                Assert.True(cell.Bottom <= scroll.VerticalOffset + scroll.ViewportHeight + 0.5);
            });
            AssertPaints(window);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DirectInlineMutationRevokesAnAlreadyIssuedLeaseWithoutWaitingForAnotherClick(bool append) => dispatcher.Run(() =>
    {
        var (window, text) = Show("literal text");
        try
        {
            text.Select(0, 7); var lease = text.RequestCopy()!;
            if (append) text.Inlines.Add(new Run(" changed"));
            else Assert.IsType<Run>(text.Inlines.FirstInline).Text = "changed text";
            Assert.False(lease.IsCurrent); Assert.Null(text.Selection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SelectionAdornerActuallyAddsBluePixelsAndIsRemovedWhenTheWindowHides() => dispatcher.Run(() =>
    {
        var (window, text) = Show("A literal text selection should be visible.");
        try
        {
            byte[] before = Pixels(window);
            text.Select(0, text.Text.Length);
            byte[] after = Pixels(window);
            Assert.Equal(before.Length, after.Length);
            Assert.Contains(Enumerable.Range(0, before.Length / 4), index =>
                after[index * 4] - after[index * 4 + 2] > 20 && before[index * 4] - before[index * 4 + 2] < 5);
            var layer = AdornerLayer.GetAdornerLayer(text)!;
            Assert.Single(layer.GetAdorners(text)!);
            window.Hide();
            Assert.True(layer.GetAdorners(text) is null or { Length: 0 });
        }
        finally { window.Close(); }
    });

    private static byte[] Pixels(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static (Window Window, NonactivatingSelectableText Text) Show(string value, double width = 280)
    {
        var text = new NonactivatingSelectableText { Text = value, DocumentIdentity = Guid.NewGuid(), FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var window = new Window { ShowActivated = false, Focusable = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            SizeToContent = SizeToContent.WidthAndHeight, Content = new Border { Width = width, MinHeight = 80, Padding = new Thickness(12),
                Background = Brushes.White, Child = text } };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        window.SourceInitialized += (_, _) => LearningPanelPlacement.Install(window);
        window.Show(); window.UpdateLayout();
        return (window, text);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
}
