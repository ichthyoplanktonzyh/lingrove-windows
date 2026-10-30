namespace Cotabby.Core.Review;

/// <summary>Owned recoverable review failures. Categories retain the original English exception
/// contract while allowing presentation to distinguish them from an arbitrary client diagnostic.
/// These values contain no endpoint response, credential, notebook or remote field data.</summary>
public enum AnkiReviewFailure
{
    InvalidResponse, Rejected, CouldNotConnect, InvalidMapping, ReviewContentTooLarge,
    MissingExample, MissingAnswer, InvalidGap, MissingCardContent, EmptyCardSides,
    InvalidAssociation, CustomizedTemplate, ProfileMismatch, MissingNote, NoteIdentityChanged,
    OperationInProgress, TemplateChanged, GapTemplateUpgradeRequired, MissingExplanationImage,
    MappedFieldsMissing, UnconfirmedFields, ManageCardTypes, DuplicateNotes, NonLocalEndpoint,
    ReviewSpeechUnavailable, SpeechDestinationChanged, AudioFieldsMissing,
    CredentialUnavailable, RequestTooLarge, ConnectionChanged, ConnectionRefreshRequired,
}

/// <summary>Source keys belong to the original review policies. The Windows credential key is
/// the platform counterpart of Mac's Keychain failure. No localization or OS call occurs here.</summary>
public static class AnkiReviewFailures
{
    public static string SourceKey(this AnkiReviewFailure failure) => failure switch
    {
        AnkiReviewFailure.InvalidResponse => AnkiWirePolicy.InvalidResponse,
        AnkiReviewFailure.Rejected => AnkiWirePolicy.Rejected,
        AnkiReviewFailure.CouldNotConnect => AnkiWirePolicy.CouldNotConnect,
        AnkiReviewFailure.InvalidMapping => "Map unique fields for the sense ID, expression and answer.",
        AnkiReviewFailure.ReviewContentTooLarge => "The review content is too large to send.",
        AnkiReviewFailure.MissingExample => "Choose a review example again.",
        AnkiReviewFailure.MissingAnswer => "Add a review answer before sending.",
        AnkiReviewFailure.InvalidGap => "Choose valid expression positions for a gap card.",
        AnkiReviewFailure.MissingCardContent => "Card content is missing. Choose its source again.",
        AnkiReviewFailure.EmptyCardSides => "Add content to both sides of the card.",
        AnkiReviewFailure.InvalidAssociation => "Choose a deck and an Anki association.",
        AnkiReviewFailure.CustomizedTemplate => "This template was customized. Manage it in Anki.",
        AnkiReviewFailure.ProfileMismatch => "Switch to the Anki profile associated with this sense.",
        AnkiReviewFailure.MissingNote => "The associated Anki note no longer exists.",
        AnkiReviewFailure.NoteIdentityChanged => "The Anki note identity changed. Check its association.",
        AnkiReviewFailure.OperationInProgress => "Wait for the current Anki operation to finish.",
        AnkiReviewFailure.TemplateChanged => "The Anki card template changed. Restore Front and Back before sending.",
        AnkiReviewFailure.GapTemplateUpgradeRequired => "Update the Lingrove template before adding explanation content to gap cards.",
        AnkiReviewFailure.MissingExplanationImage => "An explanation image is missing. Add it again before sending.",
        AnkiReviewFailure.MappedFieldsMissing => "The Anki note type does not contain the mapped fields.",
        AnkiReviewFailure.UnconfirmedFields => "Anki did not confirm the saved fields. Retry to check the note.",
        AnkiReviewFailure.ManageCardTypes => "Manage existing card types in Anki.",
        AnkiReviewFailure.DuplicateNotes => "Several Anki notes use this sense ID. Resolve duplicates in Anki.",
        AnkiReviewFailure.NonLocalEndpoint => "Use a local AnkiConnect HTTP address.",
        AnkiReviewFailure.ReviewSpeechUnavailable => "Speech is not available for review audio.",
        AnkiReviewFailure.SpeechDestinationChanged => "Speech destination is unavailable or changed. Review the speech settings before continuing.",
        AnkiReviewFailure.AudioFieldsMissing => "Map the selected audio fields or update the Lingrove template.",
        AnkiReviewFailure.CredentialUnavailable => "Could not access the Anki API key in Windows credential storage.",
        AnkiReviewFailure.RequestTooLarge => AnkiWirePolicy.RequestTooLarge,
        AnkiReviewFailure.ConnectionChanged => "Anki connection or profile changed. Refresh before sending.",
        AnkiReviewFailure.ConnectionRefreshRequired => "Refresh the Anki connection before sending.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
