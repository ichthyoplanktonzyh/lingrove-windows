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
using Cotabby.App.Localization;

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
        internal Action<ulong>? RevisionCallback;
        public IDisposable Subscribe(Action<ulong> changed) { RevisionCallback = changed; return new EmptySubscription(); }
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
    public Task LocalizedCreationKeepsEditableContentLiteral() => dispatcher.RunAsync(async () =>
    {
        InterfaceText.Initialize("zh-Hans");
        var window = new VocabularyEntryCreationWindow(new Owner());
        try
        {
            window.Show(); var entry = Find<TextBox>(window, "vocabularyNewExpression"); entry.Text = "Save";
            Assert.Equal("Save", entry.Text);
            Assert.NotEqual("Save", Find<Button>(window, "vocabularyCreateEntry").Content);
            Render(window, "entry-create-zh", 620, 590);
        }
        finally { await window.RetireAsync(); InterfaceText.Initialize("en"); }
    });
    [Fact]
    public Task ArabicCreationUsesRightToLeftWindowLayout() => dispatcher.RunAsync(async () =>
    {
        InterfaceText.Initialize("ar"); var window = new VocabularyEntryCreationWindow(new Owner());
        try
        {
            window.Show(); Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            Assert.NotEqual("Save", Find<Button>(window, "vocabularyCreateEntry").Content);
            Render(window, "entry-create-ar", 620, 590);
            window.Width = 500; window.Height = 500; window.UpdateLayout();
            Render(window, "entry-create-ar-narrow", 500, 500);
        }
        finally { await window.RetireAsync(); InterfaceText.Initialize("en"); }
    });
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
    public Task LocalizedImportCountsAndColumnsRetainLiteralInputValues() => dispatcher.RunAsync(async () =>
    {
        VocabularyWordListImportWindow? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); var reader = new DeferredReader();
            window = new(new Owner(), reader, "owned-example.txt"); window.Show();
            reader.Gate.SetResult(new VocabularyWordList(VocabularyWordListFormat.Text, [new(1, ["General {0} Cotabby"])]));
            await window.LastOperation!; window.UpdateLayout();
            Assert.Contains(Texts(window), text => text.Contains("新增词条：1", StringComparison.Ordinal) && text.Contains("新增义项：1", StringComparison.Ordinal) && text.Contains("跳过重复内容：0", StringComparison.Ordinal));
            var grid = Find<DataGrid>(window, "vocabularyImportPreview");
            Assert.Equal("General {0} Cotabby", Assert.Single(window.Session.Preview!.Inputs).Expression);
            Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "Expression"));
            Render(window, "entry-import-localized-zh", 900, 760);
        }
        finally { if (window is not null) await window.RetireAsync(); InterfaceText.Initialize("en"); }
    });

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (string value in Texts(VisualTreeHelper.GetChild(root, i))) yield return value;
    }

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
            Assert.False(Find<Button>(window, "vocabularyConfirmImport").IsEnabled); Render(window, "entry-import-error.png", 900, 760); window.Close();
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
    [Fact]
    public Task BusyDispatcherCoalescesRevisionBurstAndIgnoresLateCallbacks() => dispatcher.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); await File.WriteAllTextAsync(path, "word");
        try
        {
            long revision = 0; int previews = 0;
            var owner = new Owner { Preview = (_, _) => { previews++; return Task.FromResult(new VocabularyInputPreviewResult((ulong)Interlocked.Read(ref revision), NewResult())); } };
            var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            await session.LoadAsync(path, LearningLanguage.English); Assert.Equal(0ul, session.Preview!.Plan.Revision);
            var hooks = System.Windows.Threading.Dispatcher.CurrentDispatcher.Hooks; int posted = 0;
            System.Windows.Threading.DispatcherHookEventHandler onPosted = (_, e) =>
            { if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.DataBind) Interlocked.Increment(ref posted); };
            hooks.OperationPosted += onPosted;
            try
            {
                // Keep this UI turn blocked while a worker delivers a descending burst. Only one
                // dispatcher operation may be queued, and the maximum, not the last value, wins.
                Task.Run(() => { Interlocked.Exchange(ref revision, 1000); for (ulong i = 1000; i > 0; i--) owner.RevisionCallback!(i); }).GetAwaiter().GetResult();
                Assert.Equal(1, Volatile.Read(ref posted));
            }
            finally { hooks.OperationPosted -= onPosted; }
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (session.Preview?.Plan.Revision != 1000) { if (DateTime.UtcNow >= deadline) throw new TimeoutException(); await Task.Delay(10); }
            Assert.Equal(2, previews);
            Task.Run(() => owner.RevisionCallback!(1001)).GetAwaiter().GetResult(); session.Cancel();
            await Task.Delay(50); Assert.Equal(2, previews);
            int latePosted = 0; System.Windows.Threading.DispatcherHookEventHandler onLate = (_, e) =>
            { if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.DataBind) Interlocked.Increment(ref latePosted); };
            hooks.OperationPosted += onLate;
            try { Task.Run(() => owner.RevisionCallback!(1001)).GetAwaiter().GetResult(); Assert.Equal(0, latePosted); }
            finally { hooks.OperationPosted -= onLate; }
            Assert.False(session.Active); Assert.Null(session.Preview);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public async Task RevisionAfterDispatcherShutdownDoesNotPostOrThrow()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher; var owner = new Owner();
                _ = new VocabularyEntryImportSession(ui, owner, new WindowsVocabularyWordListReader());
                ui.InvokeShutdown(); Assert.True(ui.HasShutdownFinished);
                Task.Run(() => owner.RevisionCallback!(10)).GetAwaiter().GetResult(); completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    }
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
    private static void Render(Window window, string name, int width, int height) => VocabularyThemeRender.Capture(window, name, width, height);
}
