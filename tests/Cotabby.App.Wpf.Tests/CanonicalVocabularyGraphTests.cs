using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalVocabularyGraphTests(WpfDispatcher dispatcher)
{
    private sealed class Preferences : IVocabularyGraphPreferences { public byte[]? Data; public string BrowserMode { get; set; } = "list"; public byte[]? ReadSettings() => Data; public void WriteSettings(ReadOnlySpan<byte> data) => Data = data.ToArray(); }
    [Fact]
    public Task RealStoreEntryAndMeaningGraphNavigateAndRetire() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var added = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "shore")]); var before = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("River")]);
        var sem = before.Document.Semantics with { Nodes = [node], Mappings = [new(node.Id, added.Selection!.Value.SenseId)] }; var semantic = await store.UpdateSemanticsAsync(sem, before.Document.Semantics); var category = new R.VocabularyCategory(node.Id, R.VocabularyCategoryAxis.Content, [new("Nature")]); await store.UpdateClassificationAsync(semantic.Document.Classification with { Categories = [category], Memberships = [new(Guid.NewGuid(), node.Id, category.Id)] }, semantic.Document.Classification);
        var prefs = new Preferences(); var listPrefs = new ListPreferences(); var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), [LearningLanguage.English], listPreferences: listPrefs); window.AttachGraphPreferences(prefs); window.Show(); await window.LastOperation!; Find<Button>(window, "canonicalGraphMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => window.CanonicalGraphView is { IsVisible: true } graph && graph.Canvas.Positions.Count > 0); Assert.Equal("graph", prefs.BrowserMode); var canvas = window.CanonicalGraphView!.Canvas; var head = VocabularyGraphNodeId.Headword(added.Selection!.Value.EntryId); Assert.Contains(head, canvas.Positions.Keys); canvas.Activate(head); await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Sense(added.Selection.Value))); canvas.Activate(VocabularyGraphNodeId.Sense(added.Selection.Value)); Assert.Equal(added.Selection, window.Session.Selection); VocabularyThemeRender.Capture(window, "canonical-graph-entry", 1100, 720);
        var query = Find<TextBox>(window, "frozenNotebookSearch"); query.Text = "missing"; Assert.Null(window.Session.Selection); query.Text = ""; await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Sense(added.Selection.Value))); canvas.Activate(VocabularyGraphNodeId.Sense(added.Selection.Value)); Assert.Equal(added.Selection, window.Session.Selection);
        Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Category(category.Id))); Assert.False(canvas.IsAnimating); var unexplored = Find<CheckBox>(window, "GraphShowUnexplored"); unexplored.IsChecked = true; unexplored.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert.True(listPrefs.ShowUnexplored); canvas.Activate(VocabularyGraphNodeId.Category(category.Id)); Assert.Equal(category.Id, window.Session.Destination!.CategoryId);
        await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Meaning(node.Id))); var peer = UIElementAutomationPeer.CreatePeerForElement(canvas)!; var peers = peer.GetChildren()!; var ids = peers.Select(p => p.GetAutomationId()).ToArray(); Assert.Equal(ids.Length, ids.Distinct().Count()); Assert.Contains(ids, id => id.StartsWith("Category/")); Assert.Contains(ids, id => id.StartsWith("Meaning/")); Assert.Contains(peers, p => p.GetName() == "bank · shore"); canvas.Activate(VocabularyGraphNodeId.Meaning(node.Id)); Assert.Equal(node.Id, window.Session.Destination!.NodeId); var nodeId = VocabularyGraphNodeId.Meaning(node.Id); var original = canvas.Positions[nodeId]; var screen = canvas.Screen(original); var camera = window.CanonicalGraphView.Presentation.Camera; canvas.PointerDown(screen); canvas.PointerDrag(new Point(screen.X + 30, screen.Y + 15)); canvas.PointerUp(); Assert.Equal(original, canvas.Positions[nodeId]); Assert.NotEqual(camera.Offset, window.CanonicalGraphView.Presentation.Camera.Offset); canvas.Execute(VocabularyGraphCommand.Fit); VocabularyThemeRender.Capture(window, "canonical-graph-meaning", 1100, 720); window.Width = 760; window.Height = 500; window.UpdateLayout(); canvas.Execute(VocabularyGraphCommand.Fit); VocabularyThemeRender.Capture(window, "canonical-graph-narrow", 760, 500);
        var current = await store.ReadAsync(); var extra = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Concurrent")]); var changed = await store.UpdateClassificationAsync(current.Document.Classification with { Categories = current.Document.Classification.Categories.Append(extra).ToArray() }, current.Document.Classification); await Until(() => window.Session.Snapshot?.Revision == changed.Revision); Assert.Equal(node.Id, window.Session.Destination!.NodeId);
        await window.RetireAsync(); Assert.False(window.Session.Active); Assert.Equal(added.Selection, (await store.ReadAsync()).Document.CollectedEntries.SelectMany(e => e.Senses.Select(s => new VocabularySelection(e.Id, s.Id))).Single());
    });
    [Fact]
    public Task GraphIsDisabledWithoutPreferenceOwnerAndAttachDoesNotOpenStore() => dispatcher.RunAsync(async () =>
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-graph-lazy-" + Guid.NewGuid().ToString("N")); string path = System.IO.Path.Combine(directory, FrozenVocabularyStoreFactory.CanonicalFileName); var prefs = new Preferences();
        var list = new ListPreferences(); var feature = new CanonicalVocabularyFeature(path, new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher), System.Windows.Threading.Dispatcher.CurrentDispatcher, list, []); feature.AttachGraphPreferences(prefs); Assert.Null(feature.Window.Window); Assert.False(System.IO.Directory.Exists(directory)); await feature.DisposeAsync(); Assert.False(System.IO.Directory.Exists(directory));
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; Assert.False(Find<Button>(window, "canonicalGraphMode").IsEnabled); await window.RetireAsync();
    });
    [Fact]
    public Task HideReopenRetainsModeCameraAndSharedStore() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); await store.AddEntriesAsync([new(Expression: "word", Meaning: "sense")]); var prefs = new Preferences(); var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), hideOnClose: true); window.AttachGraphPreferences(prefs); window.Show(); await window.LastOperation!;
        Find<Button>(window, "canonicalGraphMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => window.CanonicalGraphView is { IsVisible: true } view && view.Canvas.Positions.Count > 0); var graph = window.CanonicalGraphView!; ((Slider)graph.FindName("GraphNodeSize")).Value = 21; Assert.Equal(21, VocabularyGraphSettingsCodec.Decode(prefs.Data!)!.NodeSize); graph.Canvas.Execute(VocabularyGraphCommand.ZoomIn); var camera = graph.Presentation.Camera;
        window.Close(); Assert.False(window.IsVisible); await graph.Canvas.LastSuspension; window.Show(); await Until(() => graph.IsVisible && graph.Canvas.Positions.Count > 0); Assert.Same(graph, window.CanonicalGraphView); Assert.Equal(camera, graph.Presentation.Camera); Assert.Equal("graph", prefs.BrowserMode); await window.RetireAsync(); Assert.Single((await store.ReadAsync()).Document.CollectedEntries);
    });
    [Fact]
    public Task CurrentCatalogUnexploredToggleShowsSourceTreeAndPersists() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory, installBundledClassification: true); var graphPrefs = new Preferences(); var listPrefs = new ListPreferences(); var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), listPreferences: listPrefs); window.AttachGraphPreferences(graphPrefs); window.Show(); await window.LastOperation!;
        Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; Find<Button>(window, "canonicalGraphMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => window.CanonicalGraphView is { IsVisible: true }); var check = Find<CheckBox>(window, "GraphShowUnexplored"); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert.True(listPrefs.ShowUnexplored); var canvas = window.CanonicalGraphView!.Canvas;
        await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Axis(R.VocabularyCategoryAxis.Content))); Assert.Contains(canvas.Positions.Keys, id => id.Kind == VocabularyGraphNodeKind.Category); canvas.Execute(VocabularyGraphCommand.Fit); VocabularyThemeRender.Capture(window, "canonical-graph-catalog", 1100, 720); await window.RetireAsync();
    });
    private sealed class ListPreferences : IVocabularyNotebookListPreferences { public string Density { get; set; } = "compact"; public bool ShowUnexplored { get; set; } }
    private static async Task Until(Func<bool> condition) { for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
