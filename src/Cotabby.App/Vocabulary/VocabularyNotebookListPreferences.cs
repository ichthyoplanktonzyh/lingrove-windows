using System.IO;
using Cotabby.App.Settings;

namespace Cotabby.App.Vocabulary;

/// <summary>The notebook borrows only its current list-density choice. Row expansion and
/// selection remain window state; changing density never edits the vocabulary archive.</summary>
internal interface IVocabularyNotebookListPreferences
{
    string Density { get; set; }
}

/// <summary>AppHost owns this adapter beside its other notebook preferences. Resolving settings
/// through a function follows general-settings replacement; saving presentation does not apply
/// inference settings. As with Mac AppStorage, only the exact "detailed" value enables detail.</summary>
internal sealed class VocabularyNotebookListPreferences(Func<AppSettings> current, Action persist) : IVocabularyNotebookListPreferences
{
    public string Density
    {
        get => current().VocabularyListDensity;
        set
        {
            current().VocabularyListDensity = value;
            try { persist(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
