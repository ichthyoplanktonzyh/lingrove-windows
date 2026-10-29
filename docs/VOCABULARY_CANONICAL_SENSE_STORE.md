# Canonical sense and usage transactions

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive.swift` (`createSense`, `mergeSenses`,
`moveExample`, `removeSense`), `VocabularyStore.swift`, `VocabularyNotebookProjection.swift`,
`Lingrove/Support/Vocabulary/VocabularySensePolicy.swift`, `AnkiReviewPolicy.swift`, and
`LingroveTests/Services/Vocabulary/VocabularySenseTests.swift`.

`ICanonicalVocabularySenseStore` extends the existing single canonical owner. Create and move
return acknowledged snapshots with their actual stable destination UUID. Merge and remove return
acknowledged snapshots. The serialized owner reads and edits the newest full graph under the same
exclusive database/media span, validates and commits before publication; cancellation before the
commit aborts. Source IDs and `SourceDate` values remain independent of UI state. No second Store,
model, network call or legacy-data conversion is involved.

Merge requires exact distinct source/target IDs, rejects duplicate source usage keys by their exact
framed UTF-8 identity, and preserves the target ID, source aliases, independent card IDs, bindings,
retained plans, authored notes/content and usage IDs. Multiple distinct Anki bindings require an
explicit active choice. General explanation receipts are rebound only when the source permits it;
their answer, ID and Double timestamp survive. Selected source interpretations become selected at
the target, while raw unselected evidence is not collected. Move retains the usage/source/occurrence
IDs and the empty source sense; target-less move returns the new destination. Removing a sense
removes its personal collection, not its catalog identity or raw evidence. Source replay rules and
whole-graph admission remain authoritative. Last image references use the existing identity-bound
media cleanup after SQL acknowledgement.

The pure source-derived tests cover identity, exact-ID replay, selected and unselected moves,
independent reviews/bindings, general explanation rebind and raw preservation. Named SQLite
regressions execute acknowledgements and reopen; Windows WPF-hosted isolated PNG tests exercise
last-reference cleanup. They do not execute Swift, a real Anki connection or physical notebook UI.
Semantic/category editing is a separate future transaction slice and is not activated by this API.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySenseActionTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySenseStoreTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalVocabularyEditorStoreTests|FullyQualifiedName~CanonicalWritingFavoriteViewTests'
```
