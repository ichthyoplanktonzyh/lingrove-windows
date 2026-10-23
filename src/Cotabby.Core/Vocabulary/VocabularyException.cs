namespace Cotabby.Core.Vocabulary;

/// <summary>Stable recoverable failure categories from Mac VocabularyError; no user text/path payload.</summary>
public enum VocabularyFailure { InvalidContent, UnsupportedVersion, OversizedArchive, InvalidArchive, EditConflict, StorageBusy }

/// <summary>
/// Common vocabulary boundary error. Pure policies use invalid-content/conflict; future storage
/// adapters map version/size/archive/busy failures here without leaking native error strings.
/// Existing content validation retains its narrow subtype for callers already catching it.
/// </summary>
public class VocabularyException(VocabularyFailure failure) : Exception(MessageFor(failure))
{
    public VocabularyFailure Failure { get; } = failure;
    private static string MessageFor(VocabularyFailure failure) => failure switch
    {
        VocabularyFailure.InvalidContent => "This vocabulary item could not be verified.",
        VocabularyFailure.UnsupportedVersion => "This notebook was created by a newer version of Lingrove.",
        VocabularyFailure.OversizedArchive => "The notebook exceeds the supported file size.",
        VocabularyFailure.InvalidArchive => "The notebook could not be read. The original file has been preserved.",
        VocabularyFailure.EditConflict => "This item changed while you were editing. Reopen it to continue.",
        VocabularyFailure.StorageBusy => "The notebook is busy. Try again shortly.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
