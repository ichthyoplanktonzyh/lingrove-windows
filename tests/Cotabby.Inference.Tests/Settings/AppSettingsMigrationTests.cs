using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.Tests.Settings;

/// <summary>
/// Pins the Windows preference migration as learning moves from one source to the Mac source set.
/// These tests parse the production settings class in memory; they never create a settings store,
/// start a desktop app, or read/write the current user's preferences.
/// </summary>
public sealed class AppSettingsMigrationTests
{
    [Fact]
    public void MissingSourceSet_PreservesThePreviouslyConfiguredSingleDirection()
    {
        var settings = SettingsStore.Parse("""{"LearningSourceLanguage":"French"}""");

        Assert.Equal([LearningLanguage.French], settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void FreshSettings_KeepTheEstablishedChineseDefault()
    {
        var settings = SettingsStore.Parse("{}");

        Assert.Equal([LearningLanguage.Chinese], settings.EffectiveLearningSourceLanguages);
        Assert.Equal(8_192, settings.LearningContextTokens);
    }

    [Fact]
    public void StoredContextBudget_IsPreservedWhenTheFreshDefaultIsCorrected()
    {
        var settings = SettingsStore.Parse("""{"LearningContextTokens":4096}""");

        Assert.Equal(4_096, settings.LearningContextTokens);
    }

    [Fact]
    public void ExplicitEmptySourceSet_DoesNotRestoreALegacyOrDefaultSource()
    {
        var settings = SettingsStore.Parse("""
            {"LearningSourceLanguage":"French","LearningSourceLanguages":[]}
            """);

        Assert.Empty(settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void SourceSet_TakesPrecedenceAndKeepsDistinctChoicesInUserOrder()
    {
        var settings = SettingsStore.Parse("""
            {"LearningSourceLanguage":"French","LearningSourceLanguages":["Japanese","English","Japanese"]}
            """);

        Assert.Equal([LearningLanguage.Japanese, LearningLanguage.English], settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void EffectiveSources_AreDetachedFromMutablePersistedPreferences()
    {
        var settings = new AppSettings
        {
            LearningSourceLanguages = [LearningLanguage.Japanese, (LearningLanguage)int.MaxValue, LearningLanguage.Japanese],
        };
        var snapshot = settings.EffectiveLearningSourceLanguages;

        settings.LearningSourceLanguages.Clear();

        Assert.Equal([LearningLanguage.Japanese], snapshot);
        Assert.Empty(settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void SaveFormatRoundTrip_KeepsTheFullSourceSetIncludingExplicitEmpty()
    {
        foreach (var sources in new List<LearningLanguage>[] { [], [LearningLanguage.TraditionalChinese, LearningLanguage.English] })
        {
            var original = new AppSettings { LearningSourceLanguages = sources };
            // Match SettingsStore's enum format: the old Windows settings used enum names, and the
            // new list must preserve that format rather than silently changing existing keys.
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var saved = JsonSerializer.Serialize(original, options);
            var restored = SettingsStore.Parse(saved);

            Assert.NotNull(restored.LearningSourceLanguages);
            Assert.Equal(sources, restored.EffectiveLearningSourceLanguages);
        }
    }
}
