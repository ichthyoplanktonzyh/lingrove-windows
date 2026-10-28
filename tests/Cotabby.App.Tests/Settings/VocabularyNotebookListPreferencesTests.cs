using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;

namespace Cotabby.App.Tests.Settings;

public sealed class VocabularyNotebookListPreferencesTests
{
    [Fact]
    public void DensityUsesTheOriginalKeyAndSurvivesReplacementWithoutNormalizingUnknownValues()
    {
        foreach (string raw in new[] { "null", "true", "\"future-density\"", "\"detailed\"" })
        {
            var current = SettingsStore.Parse("{\"Enabled\":false,\"vocabularyListDensity\":" + raw + "}");
            var preferences = new VocabularyNotebookListPreferences(() => current, () => { });
            string expected = raw == "\"detailed\"" ? "detailed" : raw == "\"future-density\"" ? "future-density" : "compact";
            Assert.Equal(expected, preferences.Density); Assert.False(current.Enabled);
            string before = SettingsStore.Serialize(current); current = current.ResetGeneralPreferences(10);
            using var original = JsonDocument.Parse(before); using var after = JsonDocument.Parse(SettingsStore.Serialize(current));
            bool present = original.RootElement.TryGetProperty("vocabularyListDensity", out var originalValue);
            Assert.Equal(present, after.RootElement.TryGetProperty("vocabularyListDensity", out var afterValue));
            if (present) Assert.Equal(originalValue.GetRawText(), afterValue.GetRawText());
            Assert.Equal(expected, preferences.Density);
            preferences.Density = "compact";
            Assert.Equal("compact", SettingsStore.Parse(SettingsStore.Serialize(current)).VocabularyListDensity);
        }
        var saved = new AppSettings(); var unavailable = new VocabularyNotebookListPreferences(() => saved, () => throw new IOException("fixture"));
        unavailable.Density = "detailed"; Assert.Equal("detailed", unavailable.Density);
    }
}
