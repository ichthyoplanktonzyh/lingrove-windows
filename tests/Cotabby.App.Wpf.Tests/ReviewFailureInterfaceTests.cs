using Cotabby.App.Localization;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.Core.Review;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReviewFailureInterfaceTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task KnownReviewFailuresAndMissingSourceRolesLocalizeWithoutInterpretingRemoteText() => dispatcher.RunAsync(() =>
    {
        try
        {
            foreach (string locale in new[] { "zh-Hans", "zh-Hant", "ja", "ko", "fr", "de", "es", "pt-BR", "it", "ru", "ar", "hi", "id", "tr" })
            {
                InterfaceText.Initialize(locale);
                foreach (var failure in Enum.GetValues<AnkiReviewFailure>())
                {
                    var error = new AnkiReviewException(failure);
                    Assert.NotEqual(error.Message, InterfaceFailureText.For(error));
                }
                const string raw = "AnkiConnect returned an invalid response.";
                Assert.Equal(raw, InterfaceFailureText.For(new AnkiReviewException(raw)));
                Assert.Equal("General {0} Cotabby", InterfaceFailureText.For(new AnkiReviewException("General {0} Cotabby")));
            }
            InterfaceText.Initialize("zh-Hans");
            var missing = new AnkiReviewException(AnkiReviewFailure.MissingCardContent, ReviewContentSource.Sentence);
            Assert.Equal("Card content is missing. Choose its source again. · Example sentence", missing.Message);
            Assert.Equal("卡片内容缺失，请重新选择来源。 · 例句", InterfaceFailureText.For(missing));
            Assert.Equal("无法访问 Windows 凭据存储中的 Anki API 密钥。", InterfaceFailureText.For(new AnkiReviewException(AnkiReviewFailure.CredentialUnavailable)));
        }
        finally { InterfaceText.Initialize("en"); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SharedFeatureLocalizesOwnedProbeAndKeyFailuresAndKeepsProfileAndForeignErrorsLiteral() => dispatcher.RunAsync(async () =>
    {
        try
        {
            InterfaceText.Initialize("zh-Hans"); var settings = new AppSettings(); var keys = new Keys(); var client = new Client();
            await using var feature = AnkiReviewFeature.CreateConnectionSettings(() => settings, () => { }, client, keys,
                failureText: InterfaceFailureText.For, text: key => InterfaceText.Get(key, "Vocabulary"));
            Assert.Equal(0, keys.Reads); Assert.Equal(0, client.Calls);
            feature.Settings.Address = "https://example.test";
            await feature.Controller.TestConnectionAsync();
            Assert.Equal("请使用本机 AnkiConnect HTTP 地址。", feature.Controller.ConnectionMessage);
            Assert.Equal(0, client.Calls);
            feature.Settings.Address = "http://127.0.0.1:8765";
            await feature.Controller.TestConnectionAsync();
            Assert.Equal("已连接", feature.Controller.ConnectionMessage);
            Assert.Equal("General {0} Cotabby", feature.Controller.Catalog?.Profile);
            client.Failure = new AnkiReviewException("AnkiConnect returned an invalid response.");
            await feature.Controller.TestConnectionAsync();
            Assert.Equal(client.Failure.Message, feature.Controller.ConnectionMessage); Assert.Null(feature.Controller.Catalog);
            keys.Fail = true; var interaction = new AnkiReviewSettingsInteraction(feature); interaction.ReadKeyOnAppearance();
            Assert.Equal("无法访问 Windows 凭据存储中的 Anki API 密钥。", interaction.KeyError);
        }
        finally { InterfaceText.Initialize("en"); }
    });

    private sealed class Keys : IAnkiCredentialStore
    {
        internal int Reads; internal bool Fail;
        public string ReadKey() { Reads++; return Fail ? throw new AnkiReviewException(AnkiReviewFailure.CredentialUnavailable) : "owned-fixture-key"; }
        public void SaveKey(string value) { }
    }
    private sealed class Client : IAnkiConnecting
    {
        internal int Calls; internal Exception? Failure;
        public Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
        {
            Calls++; cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } error) return Task.FromException<AnkiValue>(error);
            return Task.FromResult<AnkiValue>(action == "getActiveProfile" ? new AnkiValue.Text("General {0} Cotabby") : new AnkiValue.Sequence([]));
        }
    }
}
