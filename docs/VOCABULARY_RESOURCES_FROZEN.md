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
not a notebook decoder. A failed install leaves its input untouched. SHA-256 derived IDs use network
byte order and the original UUID version and variant bits.

Admission here is deliberately **not** a full archive gate. Lifecycle, semantic and classification
validators are separate. Full CLR representability (including all nested nullable values, enum
casts and UTF-16), lexical/alias global identity, review/template/media validation, byte codecs,
SQL relationships, transaction/CAS, v7/v8 durable migration and rollback remain required before
arbitrary notebook decoding or activation. Semantic edit/merge/reconciliation/selection rules and
classification sidebar projection remain separate work; the current validator does not implement
those operations. Loading the embedded catalog does not activate it in the application.

The focused tests adapt the source cases for 256 bundled categories and two axes, multilingual
labels, upgrades/retirement and local overlays, multi-parent deduplication/cycles, selective
ancestors, independent draft conflicts, exact mapping evidence/roles/patterns and malformed semantic
values. They do not run Swift tests, SQLite reopen/backup, a model, or a native notebook UI. Catalog
resource registration was coordinated with the primary owner. No installer, package, model download,
network transmission, application activation or user notebook access is introduced.
