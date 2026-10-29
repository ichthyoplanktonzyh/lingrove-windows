using Cotabby.App.Settings;

namespace Cotabby.App.Tests;

public sealed class AutocompleteStreamingSettingsTests
{
    [Fact]
    public void SourceDefaultsAndExplicitValuesSurviveSerialization()
    {
        var defaults = new AppSettings(); var initial = defaults.ToSuggestionSettings();
        Assert.False(initial.StreamSuggestionsWhileGenerating); Assert.True(initial.PredictAheadWhileTyping); Assert.True(initial.ShowFollowingWords);
        defaults.StreamSuggestionsWhileGenerating = true; defaults.PredictAheadWhileTyping = false; defaults.ShowFollowingWords = false;
        var restored = SettingsStore.Parse(SettingsStore.Serialize(defaults)).ToSuggestionSettings();
        Assert.True(restored.StreamSuggestionsWhileGenerating); Assert.False(restored.PredictAheadWhileTyping); Assert.False(restored.ShowFollowingWords);
        var reset = defaults.ResetGeneralPreferences(0).ToSuggestionSettings();
        Assert.False(reset.StreamSuggestionsWhileGenerating); Assert.True(reset.PredictAheadWhileTyping); Assert.True(reset.ShowFollowingWords);
        var malformed = SettingsStore.Parse("{\"lingrovePredictAheadWhileTyping\":\"bad\",\"lingroveStreamSuggestionsWhileGenerating\":7,\"lingroveShowFollowingWords\":null}");
        Assert.True(malformed.PredictAheadWhileTyping); Assert.False(malformed.StreamSuggestionsWhileGenerating); Assert.True(malformed.ShowFollowingWords);
    }
}
