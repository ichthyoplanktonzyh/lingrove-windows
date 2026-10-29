using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;
using Cotabby.App.UI.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Optional offscreen evidence in all palette modes without changing the user's system theme.</summary>
internal static class VocabularyThemeRender
{
    internal static void Capture(Window window, string name, int width, int height)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var (suffix, dark, high) in new[] { ("light", false, false), ("dark", true, false), ("highcontrast-simulated", true, true) })
            {
                VocabularyNotebookTheme.ApplyPalette(window, dark, high);
                if (high)
                {
                    foreach (string key in new[] { "WindowBg", "SidebarBg", "CardBg", "InputBg", "KeycapBg", "AccentTint", "HoverBg" }) window.Resources[key] = Brushes.Black;
                    foreach (string key in new[] { "TextPrimary", "TextSecondary", "CardBorder" }) window.Resources[key] = Brushes.White;
                    window.Resources["AccentBrush"] = Brushes.Yellow; window.Resources["OrangeBrush"] = Brushes.Yellow; window.Resources["AccentText"] = Brushes.Black;
                }
                root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(background); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "-" + suffix + ".png")); encoder.Save(stream);
            }
        }
        finally { VocabularyNotebookTheme.ApplyPalette(window, SystemTheme.IsDark, SystemParameters.HighContrast); }
    }
}
