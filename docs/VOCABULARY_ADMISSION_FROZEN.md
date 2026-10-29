# Frozen vocabulary resource admission

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. `Resources.VocabularyFrozenArchiveValidation.Validate`
accepts the historical v7-backed resource envelope; it has no file,
database, Store, network, codec or UI effects. Its internal v7 resource schema marker remains 7;
that envelope is not the native frozen v8 graph or a portable JSON representation.

| Frozen source | Windows boundary |
| --- | --- |
| `Lingrove/Services/Vocabulary/VocabularyArchive.swift` (`validate`, `validateReview`) | `VocabularyFrozenArchiveValidation.cs` and existing `VocabularyArchiveValidation.ValidateReview` |
| `VocabularyArchive+ReviewCards.swift`, `VocabularyArchive+Media.swift`, `VocabularyContentPolicy.swift`, `ReviewLayoutPolicy.swift` | shared card/layout/template/content and image metadata predicates, plus global sense-block/card IDs |
| `VocabularyLifecyclePolicy.swift`, `VocabularySemanticPolicy.swift`, `VocabularyClassificationPolicy.swift` | the resource lifecycle, semantic and classification gates composed on the joined graph |
| `VocabularyPolicy.swift`, `LearningExplanationPolicy.swift` | nullable-language entry key, unknown-completeness example key and precise general/usage explanation input construction |
| `VocabularyArchiveTests.swift`, `VocabularyLifecycleTests.swift`, `VocabularySemanticTests.swift`, `VocabularyClassificationTests.swift` | focused Core graph tests using detached values; no database or Swift execution |

A CLR representability pass checks all canonical lexical values, evidence and uncollected claims,
collections, nested explanations, content, reviews/layouts, semantic values and classification
values before joined projection. It rejects missing required object/string values, invalid enum
casts and malformed UTF-16 as `InvalidArchive`; valid Unicode noncharacters survive NFC as
combining-class-zero boundaries, including adjacent canonically equivalent text. Optional languages, optional fields and uncollected
records remain distinct. Existing immutable constructors own list/map snapshots. The semantic and
classification validators apply their frozen graph budgets and references; lifecycle checks sources,
ranges, acceptance and collection ownership. Joined lexical admission then enforces entry keys,
entry/sense/retired-alias/usage/sense-content/flexible-card identity, retained explanations, and
review/template constraints. The current Mac archive allows an entry with no senses and does not
require distinct example-content keys, unlike the older v3 validator; neither extra condition is
introduced here. Legacy review plan and flexible-card IDs retain the Mac identity scopes.

General explanation receipts preserve the uppercase `sense:<UUID>` identity for manual senses;
contextual receipts retain the source text, UTF-16 ranges and sentence/fragment/unknown state.
Matching compares the full immutable input, not only its language or display text. Shared image
validation checks metadata and budgets; it does not prove media bytes, existence or digest.
Review endpoint checks do not send a request. As in the Mac source, layout-source and review-plan
example references are resolved at use rather than newly rejected by this gate.

The Core tests cover uncollected evidence, null language, unknown-context/general receipts,
identity/alias collisions, lifecycle ranges and accepted ownership, semantic/classification
references, image metadata, review and template scopes, and malformed CLR values. They do not
execute the Swift suite or establish platform Unicode equivalence. The [frozen v8 graph and document codec](VOCABULARY_DOCUMENT_CODEC_FROZEN.md) now have their own native admission path and retain Foundation Double dates without converting them through this historical v7 value. Row projection/SQL relationships, atomic migration and rollback verification remain required before Store activation. A successful pure gate is not an archive save or native desktop acceptance.
