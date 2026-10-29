# Canonical independent review cards

Reference: macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/UI/Vocabulary/VocabularyNotebookView.swift`, `VocabularyReviewSection.swift`,
`VocabularyReviewSheet.swift`, `VocabularyReviewLayoutEditor.swift`, `VocabularyContentEditor.swift`,
`Lingrove/Support/Review/ReviewLayoutPolicy.swift`, and `LingroveTests/Services/Review/FlexibleReviewTests.swift`.

Compared the cited review view, session, controller, support and review-card archive paths against
`3b53402f2cbc196b0398d133177fd80006d165b2`; those paths have no changes from the cited revision.
Meaning/classification and graph changes elsewhere require their own module upgrade.

The notebook ReviewSection uses a new UUID for Add and the saved plan ID for Edit. A modal
`CanonicalAnkiReviewWindow` borrows the existing canonical session; name, example, front/back,
four source presets, source slots, ordered parts, sense block IDs, captions, card-only text/image
blocks and portable saved templates remain draft values until an acknowledged Store transaction.
Custom text keeps style, bold and italic. A file image is prepared by the shared Store, held as
an immutable draft and staged by the same media transaction used by Save or Save template.
Applying a template follows `CanonicalReviewCardPolicy.Applying`; it creates new part IDs and
binds matching current sense blocks. A card's plan ID is not replaced by a default review ID.

The section borrows one `ICanonicalVocabularyStore` and the existing `AnkiReviewFeature`.
Open, local removal, metadata, preview, template, save, navigation and explicit send actions use
Feature admission. Local removal requires confirmation and the captured complete card as CAS
baseline. Navigation uses the Frozen binding's endpoint/profile/note identity and the same client,
rechecking selection and connection after awaits before issuing the GUI action. Windows additionally
reads the latest Store after each remote await and rechecks that the exact card still exists and its
active binding equals the captured immutable binding. This is Windows async safety hardening; the
frozen Mac navigation does not provide this same Store recheck. Revision updates
refresh the browser. The notebook controller's `AttachReviewFeature(feature, configure)` borrows
these owners and remains inert until the notebook is shown. AppHost must call that method with its
existing Feature and settings callback; this commit does not change AppHost. Retire the notebook
controller before the Feature and canonical Store, so the modal, accepted operations and Store
subscription finish before shared owners are disposed.

The UI shows the endpoint and loaded profile before sending, requires explicit remote conflict
choices and missing-note re-add, and preserves pending IDs on failure via the session. Audio slots
are visible but speech is unavailable; sending remains blocked until the user deletes them.
Preview uses native WPF text and validated Store/prepared images, with no script, network HTML or
WebBrowser. File image import is supported; clipboard image paste and image drops in card-only
content are not yet connected. Batch preparation, review template management outside this editor,
and real Anki foreground-window activation are also incomplete. This component's GUI navigation
requests are real, but the existing connection-only Feature's native activation callback is a no-op.
Physical Anki, Shell, keyboard/accessibility, DPI and desktop acceptance remain open.

The notebook theme uses Segoe UI Variable/Segoe UI, native WPF chrome and high-contrast palette.
A light 96-DPI offscreen card form was inspected; no Mac screenshot comparison was available.
Windows WPF tests use isolated named canonical SQLite/media and fake Anki, including all four
presets, portable template save/apply, name, custom text and order, source image block identity,
file image import, audio rejection/removal, stable card identity on reopen, send receipt, notebook
Add/Edit/confirmed Remove, navigation scope/selection and latest-Store binding changes during held network reads, and modal retirement. No user notebook,
Anki server, network, model or clipboard is used.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyReviewSectionTests|FullyQualifiedName~CanonicalAnkiReviewWindowTests|FullyQualifiedName~CanonicalAnkiReviewSessionTests|FullyQualifiedName~FrozenVocabularyNotebookWindowTests'
```
