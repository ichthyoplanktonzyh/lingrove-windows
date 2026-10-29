using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class VocabularyMeaningSidebarViewTests(WpfDispatcher dispatcher)
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:x12}");
    private static F.VocabularyMeaningNode Node(int id, string title) => new(Id(id), [new(title)]);
    private static R.VocabularyCategory Category(int id, string title) => new(Id(id), R.VocabularyCategoryAxis.Content, [new(title)]);
    private static F.VocabularyEntry Entry(int id, int sense, string word) => new(Id(id), word, word, LearningLanguage.English,
        VocabularyEntryKind.Word, [new F.VocabularySense(Id(sense), "A meaning", new F.SourceDate(1e20), new F.SourceDate(1e20))], new F.SourceDate(1e20), new F.SourceDate(1e20));
    private static readonly VocabularyCategorySidebarLabels Labels = new("Categories", "Search categories", "All meanings", "Uncategorized meanings",
        "Content meanings", "Communicative functions", "New category", "Manage categories", "Edit category");

    [Fact]
    public Task MeaningRowsRenderAndSelectionExpandsUnclassifiedWithoutWriting() => dispatcher.Run(() =>
    {
        var first = Entry(1, 2, "bank"); var other = Entry(3, 4, "note"); var node = Node(5, "Finance");
        var item = new VocabularyMeaningBrowserProjection.Item(node, "Finance", [new(first.Id, Id(2))], 1);
        var sidebar = new VocabularyMeaningSidebar("Other expressions", "Suggested grouping", sense => sense.Meaning, language => language?.Name() ?? "Unspecified");
        VocabularyMeaningDestination? selected = null; sidebar.DestinationChanged += value => selected = value;
        sidebar.Present([item], [first, other], [new(other.Id, Id(4))], new(VocabularyMeaningDestinationKind.Node, node.Id), detailed: true);
        Render(sidebar, "meaning-sidebar-detailed.png");
        var list = Find<ListBox>(sidebar, "vocabularyMeaningList");
        Assert.Equal(2, list.Items.Count); Assert.Equal(node.Id, ((VocabularyMeaningDestination)((ListBoxItem)list.SelectedItem).Tag).NodeId);
        var header = list.Items.Cast<ListBoxItem>().Single(row => row.Tag is VocabularyMeaningDestination { Kind: VocabularyMeaningDestinationKind.Unclassified });
        list.SelectedItem = header;
        Assert.Equal(VocabularyMeaningDestinationKind.Unclassified, selected!.Kind);
        Assert.Equal(3, list.Items.Count);
        var senseRow = list.Items.Cast<ListBoxItem>().Single(row => row.Tag is VocabularyMeaningDestination { SenseId: not null });
        list.SelectedItem = senseRow; Assert.Equal(Id(4), selected!.SenseId);
        Assert.Equal("note", AutomationProperties.GetName(senseRow));
        Render(sidebar, "meaning-sidebar-selected.png");
        sidebar.Present([], [], [], null); Assert.Empty(list.Items); Render(sidebar, "meaning-sidebar-empty.png", expectInk: false);
    });

    [Fact]
    public Task CategoryFilterRevealsPathsAndEmitsRealIntent() => dispatcher.Run(() =>
    {
        var root = Category(10, "Root"); var child = Category(11, "Child"); var node = Node(12, "Meaning");
        var index = new R.VocabularyClassificationIndex { Categories = [root, child], Parents = [new(Id(13), child.Id, root.Id)],
            Memberships = [new(Id(14), node.Id, child.Id)] };
        var sidebar = new VocabularyCategorySidebar(Labels, new Search()); string? selected = null; Guid? create = null; bool manage = false;
        Guid? edited = null; sidebar.SelectionChanged += value => selected = value; sidebar.CreateRequested += value => create = value; sidebar.ManageRequested += () => manage = true; sidebar.EditRequested += value => edited = value;
        sidebar.Present(index, [node], [], "all"); Render(sidebar, "category-sidebar-collapsed.png");
        var list = Find<ListBox>(sidebar, "vocabularyCategoryList");
        Assert.DoesNotContain(list.Items.Cast<ListBoxItem>(), row => AutomationProperties.GetName(row) == "Child");
        var query = Find<TextBox>(sidebar, "vocabularyCategorySearch"); query.Text = "Child";
        var childRow = list.Items.Cast<ListBoxItem>().Single(row => AutomationProperties.GetName(row) == "Child");
        list.SelectedItem = childRow; Assert.Equal(child.Id.ToString("D").ToUpperInvariant(), selected);
        ((MenuItem)childRow.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal(child.Id, edited);
        Find<Button>(sidebar, "vocabularyNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(child.Id, create);
        Find<Button>(sidebar, "vocabularyManageCategories").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(manage);
        Render(sidebar, "category-sidebar-filtered.png");
        query.Text = "no category matches"; Assert.DoesNotContain(list.Items.Cast<ListBoxItem>(), row => AutomationProperties.GetName(row) == "Child");
        Render(sidebar, "category-sidebar-empty-filter.png");
        sidebar.Present(index with { Overrides = [new(child.Id) { IsHidden = true }] }, [node], [], child.Id.ToString());
        Assert.Equal("all", selected);
    });

    [Fact]
    public Task KeyboardDisclosureUsesCurrentSelectionWithoutDesktopAutomation() => dispatcher.Run(() =>
    {
        var root = Category(30, "Root"); var child = Category(31, "Child");
        var index = new R.VocabularyClassificationIndex { Categories = [root, child], Parents = [new(Id(32), child.Id, root.Id)] };
        var sidebar = new VocabularyCategorySidebar(Labels, new Search());
        using var source = new HwndSource(new HwndSourceParameters("Hidden category test") { Width = 300, Height = 440, WindowStyle = 0 });
        source.RootVisual = sidebar; sidebar.Present(index, [], [], "all"); sidebar.UpdateLayout();
        var list = Find<ListBox>(sidebar, "vocabularyCategoryList");
        list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(row => AutomationProperties.GetName(row) == "Root");
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Contains(list.Items.Cast<ListBoxItem>(), row => AutomationProperties.GetName(row) == "Child");
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Left) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.DoesNotContain(list.Items.Cast<ListBoxItem>(), row => AutomationProperties.GetName(row) == "Child");
    });
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetAutomationId(root) == id) return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindOrNull<T>(VisualTreeHelper.GetChild(root, index), id) is { } found) return found;
        throw new InvalidOperationException("Missing automation element: " + id);
    }
    private static T? FindOrNull<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetAutomationId(root) == id) return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindOrNull<T>(VisualTreeHelper.GetChild(root, index), id) is { } found) return found;
        return null;
    }
    private static void Render(FrameworkElement element, string filename, bool expectInk = true)
    {
        element.Measure(new Size(300, 440)); element.Arrange(new Rect(0, 0, 300, 440)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(300, 440, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var pixels = new byte[300 * 440 * 4]; bitmap.CopyPixels(pixels, 300 * 4, 0);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[4 * i + 3] != 0);
        if (expectInk) Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[4 * i + 3] != 0 && (pixels[4 * i] < 240 || pixels[4 * i + 1] < 240 || pixels[4 * i + 2] < 240));
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, filename)); encoder.Save(output);
        }
    }
}
