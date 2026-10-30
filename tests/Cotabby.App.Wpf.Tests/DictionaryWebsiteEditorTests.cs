using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Dictionaries;
using Cotabby.App.Settings;
using Cotabby.App.UI;
using Cotabby.App.UI.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Dictionary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class DictionaryWebsiteEditorTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WebsiteDraftValidatesBeforeSaveAndCancelLeavesCatalogUntouched(bool dark) => dispatcher.RunAsync(async () =>
    {
        var app = new AppSettings(); int writes = 0;
        var settings = new DictionarySettings(() => app, () => writes++);
        await using var books = new MDictLibraryService(s => s.ToLowerInvariant());
        var window = new DictionarySettingsWindow(settings, books) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            WindowTheme.ApplyPalette(window.Resources, dark); window.Show();
            var descriptor = DictionaryWebsiteCatalog.Websites[0];
            var editor = window.CreateWebsiteEditor(descriptor with { Name = "", Template = "https://" });
            try
            {
                WindowTheme.ApplyPalette(editor.Resources, dark); editor.Show(); editor.UpdateLayout();
                var fields = Descendants(editor).OfType<TextBox>().ToArray();
                var name = Assert.Single(fields, x => AutomationProperties.GetName(x) == "Website name");
                var template = Assert.Single(fields, x => AutomationProperties.GetName(x) == "HTTPS URL with {query}");
                var save = Assert.Single(Descendants(editor).OfType<Button>(), b => Equals(b.Content, "Save"));
                Assert.True(save.IsDefault); Assert.False(save.IsEnabled);
                name.Text = "Synthetic lookup"; template.Text = "https://{query}.invalid/";
                Assert.False(save.IsEnabled); Assert.Equal(0, writes);
                template.Text = "https://example.invalid/search?q={query}";
                Assert.True(save.IsEnabled); Assert.Equal(0, writes);
                Render(editor, dark ? "website-editor-dark.png" : "website-editor-light.png");
                LearningViewProbe.Click(save);
                Assert.False(editor.IsVisible); Assert.Equal(1, writes);
                Assert.Equal(descriptor.Id, Assert.Single(settings.Configuration.Websites).Id);
                Assert.DoesNotContain(descriptor.Source, settings.Configuration.Enabled);
            }
            finally { editor.Close(); }

            var edit = window.CreateWebsiteEditor(Assert.Single(settings.Configuration.Websites));
            try
            {
                WindowTheme.ApplyPalette(edit.Resources, dark); edit.Show();
                Assert.Single(Descendants(edit).OfType<TextBox>(), x => AutomationProperties.GetName(x) == "Website name").Text = "Discarded";
                var cancel = Assert.Single(Descendants(edit).OfType<Button>(), b => Equals(b.Content, "Cancel"));
                Assert.True(cancel.IsCancel); LearningViewProbe.Click(cancel);
                Assert.Equal("Synthetic lookup", Assert.Single(settings.Configuration.Websites).Name);
                Assert.Equal(1, writes);
            }
            finally { edit.Close(); }
            window.Width = 480; window.UpdateLayout();
            Render(window, dark ? "dictionary-sources-narrow-dark.png" : "dictionary-sources-narrow-light.png");
        }
        finally { window.Close(); await window.DisposeAsync(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew); encoder.Save(stream);
    }
}
