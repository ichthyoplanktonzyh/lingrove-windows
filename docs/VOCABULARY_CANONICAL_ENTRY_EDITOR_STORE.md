# Canonical notebook entry editing transaction

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive.swift` (`update`, `validatePersonalEdit`,
`mergeSenses`, `mergeExamples`, `correctedEvidence`), `VocabularyStore.swift`,
`Lingrove/UI/Vocabulary/VocabularyEntryEditor.swift`, `VocabularyNotebookProjection.swift`,
and `LingroveTests/Services/Vocabulary/VocabularyArchiveTests.swift` and `VocabularySenseTests.swift`.

The caller obtains the full joined entry from a revisioned `ReadAsync` snapshot, keeps that value as
its immutable baseline and edits a detached value. Cancel discards the draft. `UpdateEntryAsync`
returns the actual committed revision/document; failed saves leave the caller's draft available.
The serialized canonical owner loads the newest graph under its exclusive database/media span and
uses `VocabularyEntryDraftPolicy` for the frozen three-way merge. It retains concurrent captures,
checks the collected-sense boundary, rejects conflicting edits, keeps card/review and unrelated
fields, and updates only the source-specified timestamps using `SourceDate`. A corrected example
keeps its interpretation UUID while changed text/application forks source and occurrence identities;
a range-only correction forks the occurrence. Explicit contextual or core meaning edits become
user-authored. Removed usages are uncollected while their raw interpretation and evidence survive.
The owner normalizes, prunes stale explanations, validates the whole graph and commits with the same
identity-bound image staging, rollback and cleanup as canonical review transactions.

This bounded editor contract permits existing sense IDs and new/edited/removed usages. Creating,
merging, moving or deleting senses and category editing remain separate commands and are not
activated here. The API does not add a window, AppHost owner or legacy-data conversion. Existing
Windows image preparation limitations are recorded in `VOCABULARY_CANONICAL_REVIEW_MEDIA.md`.
The subsequent [owned-media repair](VOCABULARY_OWNED_MEDIA_REPAIR_3B.md) adds identity-bound atomic
repair on supported local NTFS/TxF while preserving explicit safe refusal elsewhere.

Source-derived Core cases cover concurrent captures and conflicting fields, uncollect versus raw
evidence, corrected evidence identities, authorship, stable timestamps, new usages and the editor's
sense scope. Isolated Windows named SQLite/WIC cases cover acknowledged save, concurrent capture,
reopen, cancellation before admission and staged-image rollback on SQL failure. They do not execute
Swift or establish physical desktop/editor UI acceptance.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyEntryDraftTests|FullyQualifiedName~FrozenVocabularyReviewDraftTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalVocabularyEditorStoreTests|FullyQualifiedName~CanonicalVocabularyMediaTests|FullyQualifiedName~CanonicalWritingFavoriteViewTests'
```
