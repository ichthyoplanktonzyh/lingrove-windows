using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual Shell COM startup/admission only. None of these tests calls native Show;
/// picker navigation, user cancellation and replacement confirmation remain desktop checks.</summary>
[Collection("WPF views")]
public sealed class VocabularyBackupDialogTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NativeBackupAndRestorePanelsRetainTheirDifferentFilePolicies(bool restore) => dispatcher.Run(() =>
    {
        string working = Environment.CurrentDirectory;
        using var dialog = new WindowsVocabularyBackupDialog(restore); var actual = dialog.ReadConfiguration();
        Assert.Equal(restore ? null : "Lingrove-vocabulary.lingrove", actual.Filename); Assert.Equal(1U, actual.TypeIndex);
        const uint common = 0x4 | 0x8 | 0x40 | 0x800;
        Assert.Equal(common, actual.Options & common);
        Assert.Equal(0U, actual.Options & (0x20 | 0x80 | 0x100 | 0x200));
        if (restore)
        {
            Assert.Equal(0x1000U, actual.Options & 0x1000); // Existing single filesystem file.
            Assert.Equal(0U, actual.Options & 0x8000); // Read-only backups remain valid inputs.
        }
        else
        {
            const uint save = 0x2 | 0x8000 | 0x10000;
            Assert.Equal(save, actual.Options & save); Assert.Equal(0U, actual.Options & 0x1000);
        }
        Assert.Equal(working, Environment.CurrentDirectory);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PreCancelledInvocationCannotShowOrPostCallback(bool restore) => dispatcher.Run(() =>
    {
        using var dialog = new WindowsVocabularyBackupDialog(restore); int posts = 0;
        Assert.Throws<OperationCanceledException>(() => dialog.Show(IntPtr.Zero, _ => posts++, new(true)));
        Assert.Equal(0, posts);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DisposedInvocationRejectsNativePresentation(bool restore) => dispatcher.Run(() =>
    {
        var dialog = new WindowsVocabularyBackupDialog(restore); dialog.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dialog.Show(IntPtr.Zero, _ => { })); dialog.Dispose();
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnownedInvocationCannotPresent(bool restore) => dispatcher.Run(() =>
    {
        using var dialog = new WindowsVocabularyBackupDialog(restore);
        Assert.Throws<ArgumentException>(() => dialog.Show(IntPtr.Zero, _ => { }));
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnotherThreadCannotAccessTheOwnerComObject(bool restore) => dispatcher.Run(() =>
    {
        using var dialog = new WindowsVocabularyBackupDialog(restore); Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => dialog.Show(IntPtr.Zero, _ => { })));
        thread.SetApartmentState(ApartmentState.MTA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionRequiresAnStaOwnerBeforeCreatingCom(bool restore)
    {
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => { using var dialog = new WindowsVocabularyBackupDialog(restore); }));
        thread.SetApartmentState(ApartmentState.MTA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
    }
}
