# Frozen vocabulary classification and semantic admission checkpoint

Reference: committed Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`.
The older v7 Resources document and its tests retain their explicit reference; the v3 runtime
archive remains an independent type and codec. This checkpoint creates no Store or durable path.

| Frozen Mac evidence | Windows mapping |
| --- | --- |
| `Lingrove/Models/Vocabulary/VocabularyClassificationModels.swift` | `Resources/VocabularyClassificationModels.cs` |
| `Lingrove/Support/Vocabulary/VocabularyClassificationPolicy.swift` | `VocabularyClassificationPolicy.cs`: bounded directory validation, effective overlays, descendants, selected graph, removal and entity-level draft merge |
| `Lingrove/Support/Vocabulary/VocabularyClassificationCatalog.swift`, `Lingrove/Resources/VocabularyClassificationCatalog.json` | `VocabularyClassificationCatalog.cs` and byte-identical embedded catalog (Git blob `ee9641a88b970b83e5822b3bf001c2a6b9aa8cb8`) |
| `Lingrove/Support/Vocabulary/VocabularySemanticPolicy.swift` | `VocabularySemanticPolicy.cs`: bounded graph validation, identity, exact sense/pattern/evidence and hierarchy checks |
| `Lingrove/Models/Vocabulary/VocabularyModels.swift` | `VocabularyFrozenDocument.cs`: isolated v8 envelope and explicit v7 resource upgrade, with classification initially empty |
| `LingroveTests/Support/Vocabulary/VocabularyClassificationTests.swift`, `LingroveTests/Services/Vocabulary/VocabularySemanticTests.swift` | focused classification/catalog and semantic tests under `tests/Cotabby.Core.Tests/Vocabulary/Resources` |

The immutable envelope retains the original v7 resource snapshot rather than re-encoding or relabeling
it; its shape is an internal Windows value, not the Mac portable JSON layout. Its explicit upgrade
rejects versions other than seven. A future v7/v8 decoder must implement and test the actual Mac
wire shape and reject unsupported versions. Categories are separate from collections and evidence;
catalog upgrades preserve retired IDs, custom children, memberships and overrides. A catalog decode
is bounded and validates the staged index; it only reads a trusted authored catalog manifest and is
not a notebook decoder. Coding keys and category axis raw strings are case-sensitive; required
arrays reject null. Malformed UTF-8, excessive nesting and invalid or numeric axes fail closed.
A failed install leaves its input untouched. SHA-256 derived IDs use network
byte order and the original UUID version and variant bits.

The [frozen resource admission gate](VOCABULARY_ADMISSION_FROZEN.md) now composes lifecycle,
semantic, classification, lexical identity and review/content validation after a CLR
representability pass. The [portable document codec](VOCABULARY_DOCUMENT_CODEC_FROZEN.md) adds bounded v7/v8 wire values. SQL relationships, transactions, durable migration and
rollback remain required before notebook activation. Semantic edit/merge/reconciliation/selection rules and
classification sidebar projection remain separate work; the current validator does not implement
those operations. Loading the embedded catalog does not activate it in the application.

The focused tests adapt the source cases for 256 bundled categories and two axes, multilingual
labels, upgrades/retirement and local overlays, multi-parent deduplication/cycles, selective
ancestors, independent draft conflicts, exact mapping evidence/roles/patterns and malformed semantic
values. They do not run Swift tests, SQLite reopen/backup, a model, or a native notebook UI. Catalog
resource registration was coordinated with the primary owner. No installer, package, model download,
network transmission, application activation or user notebook access is introduced.
