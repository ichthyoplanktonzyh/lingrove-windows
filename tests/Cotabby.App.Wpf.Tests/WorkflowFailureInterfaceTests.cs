using System.IO;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.App.UI.Learning;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class WorkflowFailureInterfaceTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task OwnedFailureCategoriesHaveTranslationsWithoutTranslatingForeignMessages() => dispatcher.RunAsync(() =>
    {
        try
        {
            var failures = Enum.GetValues<LearningTranslationFailure>().Select(f => (Exception)new LearningTranslationException(f))
                .Concat(Enum.GetValues<LearningExplanationFailure>().Select(f => new LearningExplanationException(f)))
                .Concat(Enum.GetValues<VocabularyFailure>().Select(f => new VocabularyException(f)))
                .Concat(Enum.GetValues<VocabularyExportFailure>().Select(f => new VocabularyExportException(f)))
                .Concat(Enum.GetValues<SpeechFailure>().Where(f => f != SpeechFailure.EndpointStatus).Select(f => new SpeechException(f))).ToArray();
            foreach (string locale in new[] { "zh-Hans", "zh-Hant", "ja", "ko", "fr", "de", "es", "pt-BR", "it", "ru", "ar", "hi", "id", "tr" })
            {
                InterfaceText.Initialize(locale);
                foreach (var error in failures) Assert.NotEqual(error.Message, InterfaceFailureText.For(error));
                // An arbitrary provider can return a string identical to a known catalog key.
                // Only its type/category grants the presentation permission to interpret it.
                const string foreign = "Choose at least one sense to export.";
                Assert.Equal(foreign, InterfaceFailureText.For(new InvalidOperationException(foreign)));
                Assert.Equal("General {0} Cotabby", InterfaceFailureText.For(new IOException("General {0} Cotabby")));
            }
            InterfaceText.Initialize("zh-Hans");
            Assert.Equal("请至少选择一个要导出的义项。", InterfaceFailureText.For(new VocabularyExportException(VocabularyExportFailure.Empty)));
            Assert.Equal(InterfaceFailureText.For(new VocabularyException(VocabularyFailure.EditConflict)), InterfaceFailureText.For(new MisleadingVocabularyFailure()));
            string http = InterfaceFailureText.For(new SpeechException(SpeechFailure.EndpointStatus, 503));
            Assert.Contains("503", http); Assert.DoesNotContain("%lld", http); Assert.DoesNotContain("The speech server", http);
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WordListFailureFormatsTheOriginalLineTemplateAndRetainsUnknownDiagnostics() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans");
            Assert.Equal("第 27 行缺少词条。", VocabularyInputMessages.For(new VocabularyWordListException(VocabularyWordListError.MissingExpression, 27)));
            Assert.Equal("无法导入词表。", VocabularyInputMessages.For(new VocabularyWordListException((VocabularyWordListError)999)));
            const string literal = "General {0} Cotabby.csv: provider detail";
            Assert.Equal(literal, VocabularyInputMessages.For(new IOException(literal)));
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PreviewLocalizesOnlyItsFixedFailureStateAndKeepsCapturedTextLiteral() => dispatcher.RunAsync(() =>
    {
        LearningPreviewWindow? window = null;
        try
        {
            InterfaceText.Initialize("zh-Hans"); window = new();
            var state = LearningViewProbe.Preview with { Source = "General {0} Cotabby", Result = null,
                Status = LearningPreviewStatus.Failed, ErrorMessage = new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured).Message };
            window.ShowState(state, LearningViewProbe.Anchor, false);
            Assert.Equal(state.Source, LearningViewProbe.Find<TextBlock>(window, "SourceText").Text);
            Assert.Equal("为此任务选择一个本地 GGUF 模型。", LearningViewProbe.Find<TextBlock>(window, "ErrorText").Text);
            VocabularyThemeRender.Capture(window, "workflow-failure-zh", 520, 350);
            window.ShowState(state with { ErrorMessage = "General {0} · external detail" }, LearningViewProbe.Anchor, false);
            Assert.Equal("General {0} · external detail", LearningViewProbe.Find<TextBlock>(window, "ErrorText").Text);
            Assert.Equal("讲解未能完成。请检查讲解设置后重试。", InterfaceFailureText.ExplanationState(new LearningExplanationException(LearningExplanationFailure.ProviderFailure).Message));
        }
        finally { window?.Close(); InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SharedDictionaryLocalizesStatesAndRetainsQueriesDiagnosticsAndDestination() => dispatcher.RunAsync(() =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans"); var content = new StackPanel(); var attribution = new TextBlock();
            var state = new LearningDictionaryState { ReferenceQuery = "General {0} Cotabby", Status = LearningDictionaryContentStatus.Failed,
                ErrorMessage = "The local expression dictionary is unavailable." };
            LearningDictionaryDefinitions.Populate(content, attribution, state);
            Assert.Equal("本地表达词典不可用。", Assert.Single(content.Children.OfType<TextBlock>()).Text);
            LearningDictionaryDefinitions.Populate(content, attribution, state with { ErrorMessage = "General {0} · provider detail" });
            Assert.Equal("General {0} · provider detail", Assert.Single(content.Children.OfType<TextBlock>()).Text);
            LearningDictionaryDefinitions.Populate(content, attribution, state with { Status = LearningDictionaryContentStatus.AwaitingOnlineConfirmation, SelectedSource = DictionarySource.Online });
            Assert.Contains(content.Children.OfType<TextBlock>(), value => value.Text == "在线英语词典 · https://api.dictionaryapi.dev");
            Assert.Contains(content.Children.OfType<TextBlock>(), value => value.Text == "仅将此次查询词发送到此目的地。");
            Assert.Equal("General {0} Cotabby", state.Query);
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NativeSpeechAdaptersFormatOnlyTheirFixedFailuresWithoutOpeningDevices() => dispatcher.RunAsync(async () =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans"); var systemFailure = new TaskCompletionSource<SpeechProviderEvent.Failed>(TaskCreationOptions.RunContinuationsAsynchronously);
            var voice = new SpeechVoice("owned", "Owned fixture", "en-US", SpeechVoiceQuality.Standard); var token = new SpeechPlaybackToken();
            await using (var system = new WindowsSystemSpeechProvider(() => [voice], (_, _, _, _, _, _) => throw new IOException("General {0} · private native detail"), key => InterfaceText.Get(key, "Learning")))
            {
                system.EventHandler = value => { if (value is SpeechProviderEvent.Failed failed) systemFailure.TrySetResult(failed); };
                system.Speak("Owned fixture", voice, 1, token);
                var failed = await systemFailure.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(token, failed.Token); Assert.Equal("无法播放所选系统语音。", failed.Message);
            }
            var encodedFailure = new TaskCompletionSource<SpeechProviderEvent.Failed>(TaskCreationOptions.RunContinuationsAsynchronously); int rendered = 0;
            await using (var encoded = new WindowsEncodedSpeechPlayer((_, _) => Task.FromException<WindowsSpeechDecodedAudio>(new IOException("General {0} · private decoder detail")),
                (_, _, _, _) => Interlocked.Increment(ref rendered), key => InterfaceText.Get(key, "Learning")))
            {
                encoded.EventHandler = value => { if (value is SpeechProviderEvent.Failed failed) encodedFailure.TrySetResult(failed); };
                encoded.Play(new byte[] { 1 }, 1, token);
                var failed = await encodedFailure.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(token, failed.Token); Assert.Equal("无法播放语音音频。", failed.Message); Assert.Equal(0, rendered);
            }
        }
        finally { InterfaceText.Initialize("en"); }
    });

    private sealed class MisleadingVocabularyFailure() : VocabularyException(VocabularyFailure.EditConflict)
    { public override string Message => "General {0} Cotabby"; }
}
