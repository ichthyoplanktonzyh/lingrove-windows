using Cotabby.App.Settings;
using Cotabby.Core.Models;

namespace Cotabby.App.Tests.Settings;

public sealed class ModelAssignmentSettingsTests
{
    [Fact]
    public void LegacyExplicitSelectionsRegisterPerTaskBeforeDiscoveryAndRoundTrip()
    {
        var settings = SettingsStore.Parse("""
            {"ActiveModelId":"custom:WRITE.gguf","BatteryModelId":"custom:Battery.gguf",
             "LearningLocalModel":"CUSTOM.gguf","LearningExplanationLocalModel":"tutor.gguf",
             "learningSpeech.localModel":"C:\\Models\\Voice.gguf",
             "LearningEndpointBaseUrl":"https://translate.example/", "EndpointBaseUrl":"https://complete.example/"}
            """);
        Assert.True(settings.IsLocalModelCandidate("write.gguf", LocalModelTask.Completion));
        Assert.True(settings.IsLocalModelCandidate("battery.gguf", LocalModelTask.Completion));
        Assert.True(settings.IsLocalModelCandidate("custom.gguf", LocalModelTask.Translation));
        Assert.False(settings.IsLocalModelCandidate("custom.gguf", LocalModelTask.Explanation));
        Assert.True(settings.IsLocalModelCandidate("tutor.gguf", LocalModelTask.Explanation));
        Assert.True(settings.IsLocalModelCandidate("voice.gguf", LocalModelTask.Speech));
        Assert.False(settings.IsLocalModelCandidate("discovered.gguf", LocalModelTask.Translation));
        var restored = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.True(restored.IsLocalModelCandidate("CUSTOM.gguf", LocalModelTask.Translation));
        Assert.Equal("https://translate.example/", restored.LearningEndpointBaseUrl);
        Assert.Equal("https://complete.example/", restored.EndpointBaseUrl);
    }

    [Fact]
    public void IncompatibleLegacySelectionsRemainStoredButCannotBecomeCandidates()
    {
        var settings = SettingsStore.Parse("""{"LearningExplanationLocalModel":"Hy-MT2-1.8B-Q4_K_M.gguf", "ActiveModelId":"custom:TranslateGemma-custom.gguf"}""");
        Assert.Equal("Hy-MT2-1.8B-Q4_K_M.gguf", settings.LearningExplanationLocalModel);
        Assert.Equal("custom:TranslateGemma-custom.gguf", settings.ActiveModelId);
        Assert.False(settings.IsLocalModelCandidate(settings.LearningExplanationLocalModel, LocalModelTask.Explanation));
        Assert.False(settings.IsLocalModelCandidate("TranslateGemma-custom.gguf", LocalModelTask.Completion));
    }

    [Fact]
    public void ResetPreservesAssignmentsAndSourceWhileSnapshotsDoNotAlias()
    {
        var settings = new AppSettings();
        Assert.True(settings.RecordLocalModelTask("Custom.gguf", LocalModelTask.Translation));
        settings.RecordLocalModelSource("Custom.gguf", new Uri("https://example.test/file"));
        var json = SettingsStore.Serialize(settings);
        Assert.Contains("\"lingroveLocalModelTaskAssignments\"", json);
        Assert.Contains("\"lingroveLocalModelSources\"", json);
        var snapshot = settings.ModelAssignmentSnapshot(); snapshot["custom.gguf"][0] = "speech";
        Assert.True(settings.IsLocalModelCandidate("custom.gguf", LocalModelTask.Translation));
        var reset = settings.ResetGeneralPreferences(0);
        Assert.True(reset.IsLocalModelCandidate("custom.gguf", LocalModelTask.Translation));
        Assert.Equal("https://example.test/file", reset.LocalModelSourcesValue["custom.gguf"]);
        reset.LocalModelTaskAssignmentsValue.Clear();
        Assert.True(settings.IsLocalModelCandidate("custom.gguf", LocalModelTask.Translation));
    }
}
