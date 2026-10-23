using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Compares actual interaction state against literal original save/appearance bodies.
/// The oracle records effects, not a SwiftUI runtime; feature, settings and controller are real
/// production C# here. Reflection observes the private saved-key guard without exposing it to UI.</summary>
public sealed class AnkiReviewSettingsInteractionTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-settings-interaction-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(16, rows.Length);
        Assert.Equal("4c5dbe557cfd36ea2d542c19ddeec8be4a868a1a2b60f573441c6628784790fd", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task OriginalKeyGuardAndAppearanceTrace(string id, JsonElement row)
    {
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), new SynchronizationContext());
        var current = new AppSettings(); var credentials = new AnkiTestCredentials { Key = row.GetProperty("stored").GetString()! };
        await using var feature = new AnkiReviewFeature(() => current, () => { }, store, new AnkiTestClient(), credentials);
        var view = new AnkiReviewSettingsInteraction(feature); int invalidations = 0;
        feature.Controller.PropertyChanged += (_, _) => invalidations++;
        object Snapshot() => new { key = view.Key, savedKey = (string)typeof(AnkiReviewSettingsInteraction).GetField("_savedKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!, keyError = view.KeyError,
            stored = credentials.Key, reads = credentials.Reads, writes = credentials.Writes, invalidations };
        var trace = new List<object> { Snapshot() };
        foreach (var action in row.GetProperty("actions").EnumerateArray())
        {
            switch (action.GetProperty("op").GetString())
            {
                case "appear": view.ReadKeyOnAppearance(); break;
                case "edit": view.Key = action.GetProperty("value").GetString()!; break;
                case "save": view.SaveKey(); break;
                case "readFailure": credentials.FailRead = action.GetProperty("flag").GetBoolean(); break;
                case "writeFailure": credentials.FailWrite = action.GetProperty("flag").GetBoolean(); break;
                default: throw new InvalidOperationException("Unknown interaction action");
            }
            trace.Add(Snapshot());
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
        Assert.False(store.IsLoaded);
    }
}
