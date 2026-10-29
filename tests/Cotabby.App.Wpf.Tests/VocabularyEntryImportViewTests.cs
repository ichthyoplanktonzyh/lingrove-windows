using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class VocabularyEntryImportViewTests(WpfDispatcher dispatcher)
{
    private sealed class Owner : IVocabularyEntryInputOwner
    {
        internal Func<IReadOnlyList<VocabularyEntryInput>, CancellationToken, Task<VocabularyInputPreviewResult>> Preview = (_, _) => Task.FromResult(new VocabularyInputPreviewResult(1, NewResult()));
        internal Func<IReadOnlyList<VocabularyEntryInput>, CancellationToken, Task<VocabularyInputResult>> Add = (_, _) => Task.FromResult(NewResult());
        public async Task<VocabularyInputPreview> PreviewAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken token)
        { var inputs = VocabularyWordListPolicy.Inputs(table, configuration, token); return new(inputs, await Preview(inputs, token)); }
        public IDisposable Subscribe(Action<ulong> changed) => new EmptySubscription();
        private sealed class EmptySubscription : IDisposable { public void Dispose() { } }
        public Task<VocabularyInputResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token) => Add(inputs, token);
    }
    private sealed class DeferredReader : IVocabularyWordListReader
    {
        internal readonly TaskCompletionSource<VocabularyWordList> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token;
        public Task<VocabularyWordList> ReadAsync(string path, CancellationToken token) { Token = token; return Gate.Task; }
    }
    private static VocabularyInputResult NewResult() => new(1, 1, 0, 0, 0, 0, new(Guid.NewGuid(), Guid.NewGuid()));
    [Fact]
    public Task CreationWaitsForAcknowledgementAndBlocksClose() => dispatcher.RunAsync(async () =>
    {
        var gate = new TaskCompletionSource<VocabularyInputResult>(TaskCreationOptions.RunContinuationsAsynchronously); var owner = new Owner();
        IReadOnlyList<VocabularyEntryInput>? sent = null; owner.Add = (inputs, _) => { sent = inputs; return gate.Task; };
        var window = new VocabularyEntryCreationWindow(owner); window.Show();
        Find<TextBox>(window, "vocabularyNewExpression").Text = " bank "; Render(window, "entry-create.png", 620, 590);
        Find<Button>(window, "vocabularyCreateEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("bank", sent![0].Expression); window.Close(); Assert.True(window.IsVisible);
        var result = NewResult(); gate.SetResult(result); await window.LastOperation!; Assert.Equal(result, window.Result);
    });
    [Fact]
    public Task ChangedConfigurationRejectsLatePreviewAndCommitUsesCurrentInputs() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); await File.WriteAllTextAsync(path, "expression,meaning,example,translation\nbank,shore,river bank,河岸\n");
        try
        {
            var first = new TaskCompletionSource<VocabularyInputPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
            var owner = new Owner { Preview = (_, _) => ++calls == 1 ? first.Task : Task.FromResult(new VocabularyInputPreviewResult(2, NewResult())) };
            var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            var loading = session.LoadAsync(path, LearningLanguage.English);
            while (calls == 0) await Task.Delay(10);
            var current = session.RefreshAsync(session.Configuration! with { Language = LearningLanguage.French }); await current;
            first.SetResult(new VocabularyInputPreviewResult(1, new(0, 0, 1, 0, 0, 0, null))); await loading;
            Assert.Equal(2ul, session.Preview!.Plan.Revision); Assert.Equal(LearningLanguage.French, session.Preview.Inputs[0].Language);
            Assert.Equal("河岸", session.Preview.Inputs[0].ExampleTranslation);
            var committed = await session.CommitAsync(); Assert.NotNull(committed); session.Cancel();
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task ImportRendersFiftyRowsAndDrainsCommit() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); await File.WriteAllLinesAsync(path, Enumerable.Range(1, 51).Select(i => "word" + i));
        try
        {
            var gate = new TaskCompletionSource<VocabularyInputResult>(TaskCreationOptions.RunContinuationsAsynchronously); var owner = new Owner { Add = (_, _) => gate.Task };
            var window = new VocabularyWordListImportWindow(owner, new WindowsVocabularyWordListReader(), path); window.Show(); await window.LastOperation!;
            var grid = Find<DataGrid>(window, "vocabularyImportPreview"); Assert.Equal(50, grid.Items.Count); Render(window, "entry-import.png", 900, 760);
            Find<Button>(window, "vocabularyConfirmImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Close(); Assert.True(window.IsVisible);
            gate.SetResult(NewResult()); await window.LastOperation!; Assert.NotNull(window.Result);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task RevisionDuringPreparationReplansAndMappingErrorsRemainVisible() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); await File.WriteAllTextAsync(path, "expression,example,translation\nbank,river bank,河岸\n");
        try
        {
            var gate = new TaskCompletionSource<VocabularyInputPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
            var owner = new Owner { Preview = (_, _) => ++calls == 1 ? gate.Task : Task.FromResult(new VocabularyInputPreviewResult(2, NewResult())) };
            var window = new VocabularyWordListImportWindow(owner, new WindowsVocabularyWordListReader(), path); window.Show();
            while (calls == 0) await Task.Delay(10);
            await window.Session.OwnerRevisionChangedAsync(2);
            gate.SetResult(new VocabularyInputPreviewResult(1, NewResult())); await window.LastOperation!;
            Assert.Equal(2, calls); Assert.Equal(2ul, window.Session.Preview!.Plan.Revision);
            Render(window, "entry-import-csv.png", 900, 760);
            var columns = window.Session.Configuration!.Columns.ToDictionary(); columns.Remove(VocabularyImportField.Expression);
            await window.Session.RefreshAsync(window.Session.Configuration with { Columns = columns });
            Assert.Contains("column", window.Session.Failure!, StringComparison.OrdinalIgnoreCase);
            Assert.False(Find<Button>(window, "vocabularyConfirmImport").IsEnabled); window.Close();
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task CancelRejectsLatePreviewAndOversizeFileNeverReachesOwner() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "bank"); var gate = new TaskCompletionSource<VocabularyInputPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
            var owner = new Owner { Preview = (_, _) => { calls++; return gate.Task; } };
            var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            var loading = session.LoadAsync(path, LearningLanguage.English); while (calls == 0) await Task.Delay(10);
            session.Cancel(); gate.SetResult(new VocabularyInputPreviewResult(1, NewResult())); await loading;
            Assert.Null(session.Preview); Assert.False(session.Active);
            await File.WriteAllBytesAsync(path, new byte[VocabularyWordListPolicy.MaximumBytes + 1]);
            var oversized = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            await oversized.LoadAsync(path, LearningLanguage.English);
            Assert.Contains("4 MB", oversized.Failure!); Assert.Equal(1, calls); oversized.Cancel();
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task CancelDuringReaderRejectsLateCompletion() => dispatcher.RunAsync(async () =>
    {
        var reader = new DeferredReader(); int calls = 0;
        var owner = new Owner { Preview = (_, _) => { calls++; return Task.FromResult(new VocabularyInputPreviewResult(1, NewResult())); } };
        var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, reader);
        var loading = session.LoadAsync("ignored.txt", LearningLanguage.English); session.Cancel(); Assert.True(reader.Token.IsCancellationRequested);
        reader.Gate.SetResult(new VocabularyWordList(VocabularyWordListFormat.Text, [new(1, ["bank"])])); await loading;
        Assert.Null(session.Table); Assert.Equal(0, calls);
    });
    [Fact]
    public Task HeaderToggleKeepsDisplayedKindAndCommittedInputsAligned() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); await File.WriteAllTextAsync(path, "expression\nbank\n");
        try
        {
            IReadOnlyList<VocabularyEntryInput>? submitted = null;
            var owner = new Owner { Add = (inputs, _) => { submitted = inputs; return Task.FromResult(NewResult()); } };
            var window = new VocabularyWordListImportWindow(owner, new WindowsVocabularyWordListReader(), path); window.Show(); await window.LastOperation!;
            Find<ComboBox>(window, "vocabularyImportKind").SelectedItem = VocabularyEntryKind.Sentence; await window.LastOperation!;
            Find<CheckBox>(window, "vocabularyImportHeader").IsChecked = false; await window.LastOperation!;
            Assert.Equal(VocabularyEntryKind.Sentence, Find<ComboBox>(window, "vocabularyImportKind").SelectedItem);
            Assert.All(window.Session.Preview!.Inputs, input => Assert.Equal(VocabularyEntryKind.Sentence, input.Kind));
            Find<Button>(window, "vocabularyConfirmImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
            Assert.NotNull(submitted); Assert.All(submitted!, input => Assert.Equal(VocabularyEntryKind.Sentence, input.Kind));
        }
        finally { File.Delete(path); }
    });
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T value && value.Name == name) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindOrNull<T>(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        throw new InvalidOperationException(name);
    }
    private static T? FindOrNull<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T value && value.Name == name) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindOrNull<T>(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        return null;
    }
    private static void Render(Window window, string name, int width, int height)
    {
        var element = (FrameworkElement)window.Content; element.Measure(new(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } dir)
        { Directory.CreateDirectory(dir); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(dir, name)); encoder.Save(stream); }
    }
}
