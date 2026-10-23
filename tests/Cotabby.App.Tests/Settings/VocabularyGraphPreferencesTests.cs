using System.IO;
using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Tests.Settings;

public sealed class VocabularyGraphPreferencesTests
{
    [Fact]
    public void BadPreferenceShapesDoNotDiscardUnrelatedSettingsAndOriginalKeysRoundTrip()
    {
        var settings = SettingsStore.Parse("{\"Enabled\":false,\"vocabularyGraphSettings.v1\":12,\"vocabularyBrowserMode\":true}");
        Assert.False(settings.Enabled); Assert.Null(settings.ReadVocabularyGraphSettings()); Assert.Equal("list", settings.VocabularyBrowserMode);
        settings.WriteVocabularyGraphSettings(VocabularyGraphSettingsCodec.EncodeNormalized(new() { NodeSize = 22 })); settings.VocabularyBrowserMode = "graph";
        string json = SettingsStore.Serialize(settings); using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("vocabularyGraphSettings.v1").ValueKind);
        Assert.Equal("graph", document.RootElement.GetProperty("vocabularyBrowserMode").GetString());
        var restored = SettingsStore.Parse(json); Assert.False(restored.Enabled); Assert.Equal("graph", restored.VocabularyBrowserMode);
        Assert.Equal(22, VocabularyGraphSettingsCodec.Decode(restored.ReadVocabularyGraphSettings()!)!.NodeSize);
    }
    [Fact]
    public void AdapterFollowsSettingsReplacementAndCopiesDataInBothDirections()
    {
        var old = new AppSettings(); var current = old; int saves = 0;
        var preferences = new VocabularyGraphPreferences(() => current, () => saves++);
        byte[] bytes = VocabularyGraphSettingsCodec.EncodeNormalized(new()); var frozen = bytes.ToArray(); preferences.WriteSettings(bytes); bytes[0] ^= 0xff;
        var read = preferences.ReadSettings()!; Assert.Equal(frozen, read); read[0] ^= 0xff; Assert.Equal(frozen, preferences.ReadSettings());
        current = new(); preferences.BrowserMode = "graph"; Assert.Equal("graph", current.VocabularyBrowserMode); Assert.Equal("list", old.VocabularyBrowserMode); Assert.Null(preferences.ReadSettings());
        Assert.Equal(2, saves);
    }
    [Fact]
    public void TemporaryPreferenceIoFailureRetainsTheCurrentInMemoryChoice()
    {
        var settings = new AppSettings(); var p = new VocabularyGraphPreferences(() => settings, () => throw new IOException("private path"));
        p.BrowserMode = "graph"; p.WriteSettings(VocabularyGraphSettingsCodec.EncodeNormalized(new() { NodeSize = 22 }));
        Assert.Equal("graph", p.BrowserMode); Assert.Equal(22, VocabularyGraphSettingsCodec.Decode(p.ReadSettings()!)!.NodeSize);
        var denied = new VocabularyGraphPreferences(() => settings, () => throw new UnauthorizedAccessException("private path"));
        denied.BrowserMode = "list"; Assert.Equal("list", settings.VocabularyBrowserMode);
    }
    [Fact]
    public void PresentationKeepsFrozenExpansionOneWorkerAndOnlyRelevantFocusNotifications()
    {
        var p = new VocabularyGraphPresentation(new VocabularyGraphPresentationParityTests.MemoryPreferences());
        var worker = p.Worker; Guid one = VocabularyGraphPresentationParityTests.Uuid(1), two = VocabularyGraphPresentationParityTests.Uuid(2);
        p.Toggle(one); var expanded = p.Expanded; p.Toggle(two); int focusChanges = 0;
        p.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(p.FocusedEntryId)) focusChanges++; };
        p.Toggle(one); Assert.Equal(0, focusChanges); Assert.Equal(two, p.FocusedEntryId); Assert.Single(expanded); Assert.Contains(one, expanded);
        p.Collapse(); Assert.Same(worker, p.Worker); Assert.Empty(p.Expanded); Assert.Contains(one, expanded);
        var failures = new List<Exception>();
        var otherThread = new Thread(() =>
        {
            try { p.Send(VocabularyGraphCommand.Fit); } catch (Exception error) { failures.Add(error); }
            try { p.Camera = new(); } catch (Exception error) { failures.Add(error); }
        }) { IsBackground = true };
        otherThread.Start(); Assert.True(otherThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, failures.Count); Assert.All(failures, error => Assert.IsType<InvalidOperationException>(error));
    }
}
