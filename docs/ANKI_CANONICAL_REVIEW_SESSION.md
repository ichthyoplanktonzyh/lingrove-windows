# Canonical Anki review session

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Review/AnkiReviewSession.swift`, `AnkiReviewSession+Images.swift`,
`AnkiReviewController.swift` and `AnkiReviewController+Navigation.swift`,
`Lingrove/Support/Review/ReviewCardPolicy.swift`, `ReviewLayoutPolicy.swift`, and
`LingroveTests/Services/Review/AnkiReviewTests.swift` / `AnkiReviewImageTests.swift`.
Historical library traces at other references do not establish this session's source execution.

`CanonicalReviewCardPolicy` creates detached review material directly from Frozen joined values,
including unknown language, independently owned examples, exact ranges and generated versus authored
meaning. It shares portable layout, field, HTML and wire values without converting a frozen binding
or `SourceDate` through the older DateTimeOffset graph. `CanonicalAnkiReviewSession.OpenAsync` borrows
one `ICanonicalVocabularyStore` and the existing `AnkiReviewController`; it owns a window-local plan,
baseline, conflicts and imported images. Revisioned Store notifications marshal to the opening
context and cannot regress a newer acknowledgement. Dispose detaches presentation observation,
not the Store or client. Its future host must admit and drain session work via the existing
`AnkiReviewFeature.RunAsync` before disposing the shared controller/client and then the Store.

Explicit metadata load, draft Save, binding selection, protected preset-template upgrade and Send
are available as session operations. Save applies only a real Store acknowledgement. Send uses the
existing controller's shared lease, settings, client and credentials, rechecks source material and
connection/profile across awaits, persists a pending Frozen binding before a remote note write,
searches its original stable identity on retry, and records a receipt only after verifying the note.
Conflicting remote fields need an explicit choice; a missing note needs explicit recreation. Image
bytes come from the same Store's validated media boundary and upload before note writes. A verified
remote note is followed by a separate receipt transaction even if presentation cancellation arrives.
No operation starts automatically. The future UI must show the selected endpoint/profile before an
explicit Send. This checkpoint does not connect AppHost or review UI.

Windows additionally rechecks the complete captured plan, deck, binding and mapping after awaited
reads and immediately before remote writes; accepted Save acknowledgements preserve later draft
and image edits. Metadata is published only if its captured model, binding and connection remain
current after each await. The metadata recheck is Windows async safety hardening, not a claim that
the frozen Mac method performs that same guard. Cancellation while reading an image remains
cancellation and is not reported as a missing image.

Speech is not activated: any requested review audio or audio layout slot is rejected before a remote
write, including when it appears while a metadata read is suspended. Text and image preparation do
not synthesize, upload or silently omit audio. The current Windows image subset remains documented
in `VOCABULARY_CANONICAL_REVIEW_MEDIA.md`; physical Anki, speech and desktop interactions are not
verified by these tests.

Core material tests exercise canonical unknown-language/contextual/authored selection and stable
layout block IDs. Windows WPF-hosted tests use a real isolated named canonical SQLite/media owner
and synthetic Anki, covering saved pending identity, lost reply/reopen/merge recovery, explicit
remote conflict resolution, image upload and missing image, source/scope and audio guards, cancellation
after verification, shared controller lease, notification retirement, template upgrade and admitted
send completion after view retirement. Portable historical controller/session tests verify the
legacy guard and helper extraction; they do not establish canonical source parity or UI activation.
The new source is also compiled into portable App tests. No network request, user notebook, model or
release is used by the tests.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalReviewCardPolicyTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalAnkiReviewSessionTests|FullyQualifiedName~AnkiReviewSessionNativeTests|FullyQualifiedName~AnkiReviewSettingsViewTests'
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --filter 'FullyQualifiedName~AnkiReviewControllerTests|FullyQualifiedName~AnkiReviewSessionTests|FullyQualifiedName~AnkiReviewFeatureTests|FullyQualifiedName~AnkiReviewSettingsInteractionTests'
```
