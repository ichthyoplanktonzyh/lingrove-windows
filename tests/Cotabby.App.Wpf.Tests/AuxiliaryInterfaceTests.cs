using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Learning;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Models;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class AuxiliaryInterfaceTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task CandidateChromeLocalizesButDictionaryValuesAndDiagnosticsStayLiteral() => dispatcher.RunAsync(() =>
    {
        Window? window = null; ExpressionAssistNoticeWindow? notice = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); const string literal = "General {0} Cotabby";
            var view = new ExpressionAssistView();
            view.Update(new ExpressionAssistResult([new("close over", "Close", literal)], null), null, false);
            window = new Window { Content = view, Width = 480, Height = 420, ShowActivated = false };
            VocabularyNotebookTheme.Follow(window); window.Show(); window.UpdateLayout();
            Assert.Contains(Nodes(view).OfType<TextBlock>(), text => text.Text.Contains(literal, StringComparison.Ordinal) && !text.Text.StartsWith("Base form:", StringComparison.Ordinal));
            var candidate = Assert.Single(Nodes(view).OfType<Button>());
            Assert.Contains(literal, AutomationProperties.GetName(candidate));
            Assert.Contains("Close", AutomationProperties.GetName(candidate));
            Assert.DoesNotContain("Base form:", AutomationProperties.GetName(candidate));
            notice = new ExpressionAssistNoticeWindow("Provider detail: Close {0}", literal, _ => throw new InvalidOperationException("No copy was authorized."));
            notice.ShowAt(new ScreenRect(80, 80, 1, 18)); notice.UpdateLayout();
            var value = Assert.Single(Nodes(notice).OfType<NonactivatingSelectableText>());
            Assert.Equal(literal, value.Text); Assert.Equal("候选：" + literal, AutomationProperties.GetName(value));
            Assert.Contains(Nodes(notice).OfType<TextBlock>(), text => text.Text == "Provider detail: Close {0}");
            VocabularyThemeRender.Capture(window, "auxiliary-expression-zh", 480, 420);
        }
        finally { notice?.Close(); window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModelManagementFollowsTheWindowPaletteWithoutReinterpretingModelNames() => dispatcher.RunAsync(() =>
    {
        Window? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); const string filename = "General {0} Cotabby.gguf";
            var settings = new AppSettings { LearningLocalModel = filename }; settings.RecordLocalModelTask(filename, LocalModelTask.Translation);
            var view = new ModelManagementView("Translation", new TextBlock { Text = "Owned catalog" }, () => { }, () => { }, null);
            view.ShowModels([new LearningInstalledModel(filename, "General {0} Cotabby", 1)], settings, LocalModelTask.Translation, filename);
            window = new Window { Content = view, Width = 720, Height = 640, ShowActivated = false };
            VocabularyNotebookTheme.Follow(window); window.Show(); window.UpdateLayout();
            VocabularyNotebookTheme.ApplyPalette(window, dark: true, highContrast: false);
            Assert.Same(window.FindResource("WindowBg"), view.Background);
            Assert.Same(window.FindResource("Btn"), view.Done.Style);
            Assert.Contains(Nodes(view).OfType<TextBlock>(), text => text.Text == filename);
            Assert.Equal(filename, settings.LearningLocalModel);
            VocabularyThemeRender.Capture(window, "auxiliary-models-zh", 720, 640);
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Nodes(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
