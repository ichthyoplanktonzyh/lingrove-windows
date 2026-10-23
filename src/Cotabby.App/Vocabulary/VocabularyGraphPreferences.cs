using System.IO;
using Cotabby.App.Settings;

namespace Cotabby.App.Vocabulary;

/// <summary>Presentation borrows this narrow preference boundary instead of the composition root
/// or durable notebook. Implementations copy the graph Data payload; browser mode remains the
/// original string (only "graph" enables graph browsing). No graph camera or expansion is durable.</summary>
internal interface IVocabularyGraphPreferences
{
    byte[]? ReadSettings();
    void WriteSettings(ReadOnlySpan<byte> data);
    string BrowserMode { get; set; }
}

/// <summary>AppHost-owned adapter over its existing settings owner. Reading through a function
/// follows settings resets instead of retaining an obsolete AppSettings instance. Saving notebook
/// preferences does not apply autocomplete/runtime settings or start a model.</summary>
internal sealed class VocabularyGraphPreferences(Func<AppSettings> current, Action persist) : IVocabularyGraphPreferences
{
    public byte[]? ReadSettings() => current().ReadVocabularyGraphSettings();
    public void WriteSettings(ReadOnlySpan<byte> data) { current().WriteVocabularyGraphSettings(data); Save(); }
    public string BrowserMode { get => current().VocabularyBrowserMode; set { current().VocabularyBrowserMode = value; Save(); } }
    private void Save()
    {
        // UserDefaults is best effort and the original view has no preference-save error UI.
        // Keep the in-memory choice if the Windows settings destination is temporarily unwritable.
        try { persist(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
