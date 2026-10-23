using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real Shell COM startup/configuration on STA, deliberately without Show or file effects.
/// Interactive picker navigation/cancel still requires a Windows desktop validation pass.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportDialogTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(VocabularyExportFormat.Text, false, true, VocabularyExportTextStyle.Notes, "txt")]
    [InlineData(VocabularyExportFormat.Csv, false, true, VocabularyExportTextStyle.Notes, "csv")]
    [InlineData(VocabularyExportFormat.Json, false, true, VocabularyExportTextStyle.Notes, "json")]
    [InlineData(VocabularyExportFormat.Text, true, true, VocabularyExportTextStyle.Notes, "zip")]
    [InlineData(VocabularyExportFormat.Csv, true, true, VocabularyExportTextStyle.Notes, "zip")]
    [InlineData(VocabularyExportFormat.Json, true, true, VocabularyExportTextStyle.Notes, "zip")]
    [InlineData(VocabularyExportFormat.Text, true, true, VocabularyExportTextStyle.Expressions, "txt")]
    [InlineData(VocabularyExportFormat.Json, true, false, VocabularyExportTextStyle.Notes, "json")]
    public Task NativeShellConfigurationUsesOnlyCapturedFormat(VocabularyExportFormat format, bool images, bool content, VocabularyExportTextStyle style, string extension) => dispatcher.Run(() =>
    {
        string working = Environment.CurrentDirectory;
        using var dialog = new WindowsVocabularyExportDialog(new() { Format = format, Images = images, Content = content, TextStyle = style });
        var actual = dialog.ReadConfiguration();
        Assert.Equal("Lingrove-vocabulary." + extension, actual.Filename); Assert.Equal(1U, actual.TypeIndex);
        const uint required = 0x2 | 0x4 | 0x8 | 0x40 | 0x800 | 0x8000 | 0x10000;
        Assert.Equal(required, actual.Options & required); Assert.Equal(0U, actual.Options & (0x20 | 0x200 | 0x1000));
        Assert.Equal(working, Environment.CurrentDirectory);
    });
    [Fact]
    public Task PreCancelledPickerNeverShowsOrDispatchesCallback() => dispatcher.Run(() =>
    {
        using var dialog = new WindowsVocabularyExportDialog(new()); int posts = 0;
        Assert.Throws<OperationCanceledException>(() => dialog.Show(IntPtr.Zero, _ => posts++, new(true))); Assert.Equal(0, posts);
    });
    [Fact]
    public Task DisposedDialogRejectsAnotherNativeInvocation() => dispatcher.Run(() =>
    {
        var dialog = new WindowsVocabularyExportDialog(new()); dialog.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dialog.Show(IntPtr.Zero, _ => { })); dialog.Dispose();
    });
    [Fact]
    public Task UnownedInvocationIsRejectedBeforeModalPresentation() => dispatcher.Run(() =>
    {
        using var dialog = new WindowsVocabularyExportDialog(new());
        Assert.Throws<ArgumentException>(() => dialog.Show(IntPtr.Zero, _ => { }));
    });
}
