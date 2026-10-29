# Canonical semantic and classification transactions

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/Vocabulary/VocabularyArchive+Semantics.swift`, `VocabularyStore.swift`,
`Lingrove/Support/Vocabulary/VocabularySemanticPolicy.swift`, `VocabularyClassificationPolicy.swift`,
and `LingroveTests/Services/Vocabulary/VocabularySemanticTests.swift` and
`VocabularyClassificationPersistenceTests.swift`. This checkpoint explicitly aligns these
transactions with 35b; historical resource-policy checks keep their own recorded references.

`ICanonicalVocabularySemanticStore` supplies distinct semantic and classification operations on
the one canonical owner. Each accepts an immutable edited index and its captured baseline. Under
the serialized database/media span, semantic nodes, patterns, mappings, relations, cues and cue
targets merge individually by UUID; unrelated concurrent edits survive and conflicting edits fail.
The source permits an already-removed semantic row when removing the same baseline row. Deleting a
baseline semantic node removes its classification memberships, retaining categories and overrides.
The classification transaction merges categories, parent edges, memberships and overrides by UUID,
while preserving current framework rows and refusing edits to baseline bundled definitions or their
parent edges. It reuses `Resources.VocabularyClassificationPolicy.Merge` after comparing its body to
35b, and validates against actual frozen semantic node IDs. Frozen semantic nodes are never converted
to the older DateTimeOffset node model; their `SourceDate` Double values remain exact.

Both operations validate the corresponding graph and complete current document before SQL, return
an acknowledged revisioned snapshot, and publish only after commit. As in the source, an unchanged
draft still commits a revision. The independently loaded bundled catalog has no inferred memberships;
custom categories, overrides and explicit memberships remain user state. Failed operations do not
publish their candidate. The first lazy catalog installation can itself commit independently before
a later edit fails. Existing media reference protections and Store lifetime/cancellation apply.

Source-derived Core tests cover independent and conflicting row changes, concurrent removal,
Double timestamps, membership cleanup, bundled definition/parent protection, custom overrides,
unknown nodes and graph cycles. Real isolated named SQLite tests cover both APIs, independent
concurrent drafts, no-op revision, injected SQL rollback and reopen. They do not execute Swift,
activate category UI or read user notebooks. No legacy data or cross-platform file migration is
part of this checkpoint.

The isolated Windows delivery branch was independently reviewed and then built with
`dotnet build Cotabby.sln -c Release --no-restore`: zero warnings and errors. The two
filtered commands below passed Core 5/5 and Inference 2/2 on Windows x64. These checks
exercise the canonical transactions against isolated files; they do not activate the
semantic/category editor or establish physical desktop acceptance.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySemanticDraftTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySemanticStoreTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingFavoriteViewTests|FullyQualifiedName~CanonicalVocabularyEditorStoreTests'
```
