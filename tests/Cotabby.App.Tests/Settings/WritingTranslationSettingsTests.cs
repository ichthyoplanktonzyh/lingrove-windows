using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Tests.Settings;

public sealed class WritingTranslationSettingsTests
{
    [Fact]
    public void DraftReferenceIsExplicitAndUnknownValuesRetainTheSafeDefault()
    {
        var settings = new AppSettings(); Assert.Equal(LearningReferenceMode.CurrentSentence, settings.LearningReferenceMode);
        settings.LearningReferenceMode = LearningReferenceMode.Draft;
        var restored = SettingsStore.Parse(SettingsStore.Serialize(settings)); Assert.Equal(LearningReferenceMode.Draft, restored.LearningReferenceMode);
        var unknown = SettingsStore.Parse("{\"learningReferenceMode\":\"future\"}");
        Assert.Equal(LearningReferenceMode.CurrentSentence, unknown.LearningReferenceMode);
        Assert.Equal("future", unknown.LearningReferenceModeValue!.Value.GetString());
    }
}
