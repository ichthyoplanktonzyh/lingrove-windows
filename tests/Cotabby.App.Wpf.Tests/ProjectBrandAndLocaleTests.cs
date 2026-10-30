using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Branding;
using Cotabby.App.Localization;
using Cotabby.App.Reading;
using Cotabby.App.UI.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ProjectBrandAndLocaleTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task InitialXamlHeadersTooltipsAndAccessibleNamesTranslateWithoutReplacingBindings() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans");
            var authored = new Expander { Header = "Close", ToolTip = "Close" };
            AutomationProperties.SetName(authored, "Close");
            var bound = new Expander();
            foreach (var property in new[] { HeaderedContentControl.HeaderProperty, FrameworkElement.ToolTipProperty, AutomationProperties.NameProperty })
                bound.SetBinding(property, new Binding("Caption") { Source = new { Caption = "Close" } });
            var root = new StackPanel { Children = { authored, bound } };
            InterfaceText.TranslateInitialTree(root);
            Assert.Equal("关闭", authored.Header); Assert.Equal("关闭", authored.ToolTip); Assert.Equal("关闭", AutomationProperties.GetName(authored));
            Assert.Equal("Close", bound.Header); Assert.Equal("Close", bound.ToolTip); Assert.Equal("Close", AutomationProperties.GetName(bound));
            Assert.True(BindingOperations.IsDataBound(bound, HeaderedContentControl.HeaderProperty));
            var window = new Window { Title = "Replace notebook?" };
            try
            {
                InterfaceText.TranslateInitialTree(window, "Vocabulary");
                Assert.Equal("恢复生词本？", window.Title);
            }
            finally { window.Close(); }
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task MacTablesRetainContextAndTrustedBrandAdaptationDoesNotRewriteArguments() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans");
            Assert.Equal("需要处理", InterfaceText.Get("Needs attention", "Learning"));
            Assert.Equal("需要注意", InterfaceText.Get("Needs attention", "Localizable"));
            Assert.Equal("版本 General", InterfaceText.Format("Version {0}", "General"));
            Assert.Equal("服务端点 · Cotabby {General}", InterfaceText.Format("Endpoint · {0}", "Cotabby {General}"));
            Assert.Contains("Cotabby", InterfaceText.Get("Original Cotabby project"));
            InterfaceText.Initialize("ja");
            Assert.Equal("Lingroveについて", InterfaceText.Get("About", "Localizable"));
            if (Environment.GetEnvironmentVariable("LINGROVE_BRAND_RENDER_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                foreach (string locale in new[] { "en", "zh-Hans", "ar" })
                {
                    InterfaceText.Initialize(locale);
                    var window = new Window { Content = Cotabby.App.UI.Settings.Panes.Branding.Identity("Autocomplete and language learning, locally."),
                        Width = 550, Height = 110, Background = Brushes.White };
                    try
                    {
                        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
                        InterfaceText.ApplyDirection(window); var content = (FrameworkElement)window.Content;
                        window.Content = null;
                        // The outer LTR surface captures its child's complete RTL layout rather
                        // than omitting an unshown Window's ancestor transform or background.
                        var surface = new Border { Width = 550, Height = 110, Background = Brushes.White, FlowDirection = FlowDirection.LeftToRight,
                            Child = new Border { Child = content, FlowDirection = window.FlowDirection } };
                        surface.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
                        surface.Measure(new Size(550, 110)); surface.Arrange(new Rect(0, 0, 550, 110)); surface.UpdateLayout();
                        var image = new RenderTargetBitmap(550, 110, 96, 96, PixelFormats.Pbgra32); image.Render(surface);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var output = File.Create(Path.Combine(directory, "identity-" + locale + ".png")); encoder.Save(output);
                    }
                    finally { window.Close(); }
                }
            }
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReadingLocalizesOnlyChromeAndPreservesSourceResultAndDestination() => dispatcher.RunAsync(() =>
    {
        ReadingWindow? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); window = new ReadingWindow();
            var selection = new ReadingSelection(Guid.NewGuid(), "Close", 1, "field", 1, 0, 5, false, true,
                InputCompositionState.Clear, 1, ScreenRect.Empty);
            window.Update(new ReadingState(selection, null, LearningLanguage.Chinese,
                new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "General / Cotabby", "Dictionary"));
            var labels = Descendants<TextBlock>(window).Select(item => item.Text).ToArray();
            Assert.Contains("选中文本翻译", labels); Assert.Contains("Close", labels);
            Assert.Contains("Dictionary", labels); Assert.Contains("General / Cotabby", labels);
            Assert.Contains(Descendants<Button>(window), button => Equals(button.Content, "关闭"));
            Assert.False(window.IsVisible);
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ApprovedPngIconFramesAndRuntimeAssemblyIdentityAreAvailable() => dispatcher.RunAsync(() =>
    {
        Assert.Equal("Cotabby", typeof(AppBrand).Assembly.GetName().Name);
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Cotabby;component/Assets/Lingrove.ico"))!.Stream;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); byte[] data = buffer.ToArray();
        Assert.Equal(1, BitConverter.ToUInt16(data, 2)); Assert.Equal(5, BitConverter.ToUInt16(data, 4));
        for (int index = 0; index < 5; index++)
        {
            int start = 6 + index * 16; int length = BitConverter.ToInt32(data, start + 8); int offset = BitConverter.ToInt32(data, start + 12);
            Assert.InRange(offset, 86, data.Length - length);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, data.AsSpan(offset, 8).ToArray());
        }
        return Task.CompletedTask;
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
