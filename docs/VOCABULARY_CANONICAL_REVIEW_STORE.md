# Canonical review Store transactions

At frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive.swift` and `VocabularyArchive+ReviewCards.swift`
implement the five review, receipt and template edits; `VocabularyStore.swift` applies their
acknowledged snapshots. `VOCABULARY_CANONICAL_REVIEW_DRAFT.md` describes the pure rules.

`ICanonicalVocabularyReviewStore` is inherited by the one canonical Store. Each method returns the
actual committed revision and immutable document, or propagates an error. Under the Store's worker
and exclusive database span it reads the latest graph, applies the draft, checks media, validates,
commits and publishes after acknowledgement. Cancellation before SQL commit aborts; a completed
commit is returned and cannot be revoked by cancellation. Missing-template removal still commits.
Lazy classification installation may itself commit when the first operation loads the Store; it is a
separate acknowledged installation even if the following review edit fails.

This checkpoint is deliberately text-only relative to Mac: nonempty supplied image maps are rejected,
and any change to the set of image references across content, all current and preserved review
layouts and templates is rejected. Removing the last reference also requires the missing media owner.
Unchanged references are left alone and are not evidence of media-file admission. No staging, file
validation, cleanup or image-backed operation is claimed. Review audio receipts are detached Anki
remote identities (fingerprints, endpoint/profile and filename), not local file references; they
contain no audio bytes and this Store does not generate, read, upload or delete audio. Their existing
frozen archive validation applies. Review layouts have no other external-file content cases.

Isolated Windows SQLite tests cover real commit/reopen, independent and competing card drafts,
revision acknowledgements, rollback, cancellation, missing-template commit, and explicit media
refusal. The seeded old-reference test checks refusal only and does not establish image-file validity.
There is no review UI, controller, network or production activation in this checkpoint.

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyReviewStoreTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingFavoriteViewTests'
```

The author executed the isolated Store suite (5/5), writing bridge suite (6/6) and Win32 Release
build (zero warnings). Independent review compared the frozen archive functions and the new
policy/transaction code, and reran Core draft tests 3/3 and isolated Store tests 5/5.
After integration with the canonical AppHost composition, `dotnet build Cotabby.sln -c Release
--no-restore --verbosity quiet` passed with zero warnings/errors. These compiled suites then ran:

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~FrozenVocabularyReviewDraftTests' --verbosity minimal
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~FrozenVocabularyReviewStoreTests|FullyQualifiedName~CanonicalWritingStoreTests|FullyQualifiedName~FrozenVocabularyClassificationStoreTests' --verbosity minimal
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CanonicalVocabularyCompositionTests|FullyQualifiedName~CanonicalWritingFavoriteViewTests|FullyQualifiedName~AnkiReviewSettingsViewTests' --verbosity minimal
```

They passed 3/3, 18/18 and 15/15 respectively on Windows x64 with .NET SDK 10.0.403. This does not
activate canonical review controls or establish physical Anki, media, Swift execution or desktop
acceptance.
