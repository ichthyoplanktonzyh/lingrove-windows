using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Reads actual view state and native styles without synthesizing global input. Routed Click
/// exercises event wiring/identity, not physical mouse hit testing or external-editor focus.
/// Bitmap rendering stays in memory; it detects an empty visual, not typographic pixel parity.
/// </summary>
internal static class LearningViewProbe
{
    public static readonly ScreenRect Anchor = new(80, 80, 1, 18);
    public static T Find<T>(Window window, string name) where T : FrameworkElement =>
        Assert.IsType<T>(window.FindName(name));
    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    public static string Text(TextBlock block) => new TextRange(block.ContentStart, block.ContentEnd).Text;

    public static LearningUnit Unit => new(0, "keep me posted", "keep someone posted", LearningUnitKind.Pattern,
        [new(0, 4), new(8, 6)], [new("keep", new(0, 4)), new("me", new(5, 2)), new("posted", new(8, 6))]);
    public static LearningExplanationInput Input => new("keep me posted", "keep someone posted", LearningLanguage.English,
        VocabularyEntryKind.Pattern, "keep me posted", [new VocabularyTextRange(0, 4), new(8, 6)], LearningExplanationContext.Sentence);
    public static LearningExplanationState Explanation(string meaning = "Keep me informed.", string prose = "Use this when asking for updates.")
    {
        var input = Input;
        return new() { Input = input, Record = new(Guid.NewGuid(), input, LearningLanguage.English,
            new(meaning, prose), "test-local", DateTimeOffset.UnixEpoch) };
    }
    public static LearningDictionaryState Dictionary => new()
    {
        DocumentId = "doc-a", Unit = Unit, Page = LearningDictionaryPage.Contextual,
        HasContextualExplanation = true, ExplanationConfigured = true,
        ExplanationLanguage = LearningLanguage.English, Explanation = Explanation(),
    };
    public static LearningPreviewState Preview => new()
    {
        RequestId = "doc-a", Source = "请让我知道最新情况。", Result = "keep me posted", Status = LearningPreviewStatus.Ready,
        Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
        Units = [Unit], UnitStage = LearningUnitStage.Ready,
    };

    public static void AssertNonactivating(Window window)
    {
        Assert.True(window.IsVisible);
        Assert.False(window.ShowActivated);
        Assert.False(window.Focusable);
        Assert.False(window.ShowInTaskbar);
        int style = GetWindowLong(new WindowInteropHelper(window).Handle, -20);
        Assert.Equal(0x08000080, style & 0x08000080); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
    }

    public static void AssertPaints(Window window)
    {
        window.UpdateLayout();
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        Assert.InRange(root.ActualWidth, 100, 500);
        Assert.InRange(root.ActualHeight, 50, 700);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] > 0);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
}
