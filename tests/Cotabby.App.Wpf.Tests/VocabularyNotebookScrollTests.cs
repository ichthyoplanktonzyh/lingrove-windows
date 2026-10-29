using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class VocabularyNotebookScrollTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ScopedScrollbarsRetainWheelPageAndThumbForBothAxes() => dispatcher.Run(() =>
    {
        var scroll = new ScrollViewer { Width = 240, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 1000, Height = 1000 } };
        var window = new Window { Width = 300, Height = 220, Content = scroll }; VocabularyNotebookTheme.Follow(window); window.Show(); window.UpdateLayout();
        var bars = Descendants(scroll).OfType<ScrollBar>().ToArray(); var vertical = bars.Single(b => b.Orientation == Orientation.Vertical); var horizontal = bars.Single(b => b.Orientation == Orientation.Horizontal);
        Assert.Same(window.Resources[typeof(ScrollBar)], vertical.Style); Assert.Same(window.Resources[typeof(ScrollBar)], horizontal.Style);
        var lineDown = (RepeatButton)vertical.Template.FindName("lineForward", vertical); var lineRight = (RepeatButton)horizontal.Template.FindName("lineForward", horizontal);
        Assert.Same(ScrollBar.LineDownCommand, lineDown.Command); Assert.Same(ScrollBar.LineRightCommand, lineRight.Command);
        ScrollBar.LineDownCommand.Execute(null, lineDown); ScrollBar.LineRightCommand.Execute(null, lineRight); window.UpdateLayout(); Assert.True(scroll.VerticalOffset > 0); Assert.True(scroll.HorizontalOffset > 0);
        scroll.ScrollToVerticalOffset(0); scroll.ScrollToHorizontalOffset(0); window.UpdateLayout();
        Assert.True(ScrollBar.PageDownCommand.CanExecute(null, vertical)); ScrollBar.PageDownCommand.Execute(null, vertical); window.UpdateLayout(); Assert.True(scroll.VerticalOffset > 0);
        Assert.True(ScrollBar.PageRightCommand.CanExecute(null, horizontal)); ScrollBar.PageRightCommand.Execute(null, horizontal); window.UpdateLayout(); Assert.True(scroll.HorizontalOffset > 0);
        scroll.ScrollToVerticalOffset(0); window.UpdateLayout(); scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent }); window.UpdateLayout(); Assert.True(scroll.VerticalOffset > 0);
        scroll.ScrollToVerticalOffset(0); window.UpdateLayout();
        var source = PresentationSource.FromVisual(window)!;
        scroll.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.PageDown) { RoutedEvent = Keyboard.KeyDownEvent }); window.UpdateLayout(); Assert.True(scroll.VerticalOffset > 0);
        var track = (Track)vertical.Template.FindName("PART_Track", vertical); var thumb = track.Thumb; var before = scroll.VerticalOffset;
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); thumb.RaiseEvent(new DragDeltaEventArgs(0, 35) { RoutedEvent = Thumb.DragDeltaEvent }); thumb.RaiseEvent(new DragCompletedEventArgs(0, 35, false) { RoutedEvent = Thumb.DragCompletedEvent }); window.UpdateLayout(); Assert.True(scroll.VerticalOffset > before);
        var horizontalTrack = (Track)horizontal.Template.FindName("PART_Track", horizontal); Assert.False(horizontalTrack.IsDirectionReversed); Assert.True(track.IsDirectionReversed);
        before = scroll.HorizontalOffset; var horizontalThumb = horizontalTrack.Thumb;
        horizontalThumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); horizontalThumb.RaiseEvent(new DragDeltaEventArgs(35, 0) { RoutedEvent = Thumb.DragDeltaEvent }); horizontalThumb.RaiseEvent(new DragCompletedEventArgs(35, 0, false) { RoutedEvent = Thumb.DragCompletedEvent }); window.UpdateLayout(); Assert.True(scroll.HorizontalOffset > before);
        window.Close();
        var other = new Window { Content = new ScrollViewer { Content = new Border { Height = 1000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Visible }, Width = 200, Height = 150 }; other.Show(); other.UpdateLayout();
        Assert.NotSame(vertical.Style, Descendants(other).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical).Style); other.Close();
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
