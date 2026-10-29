# Latest independent vocabulary resources

Reference: committed Mac `9290066ef7f20a50068ce820f477737fdfc2b8b0`. The lexical/resource
models, lifecycle rule and projection are unchanged from `06bf81fe`; the latest target also includes
the committed readability update and classification terminology. This adapts the actual Mac types
and rules; it does not introduce a Windows classifier or new product model.

| Mac source | Windows boundary |
| --- | --- |
| `Lingrove/Models/Vocabulary/VocabularyModels.swift` | `Core/Vocabulary/Resources/VocabularyResourceModels.cs`: nullable study language, source/occurrence IDs, lexical/joined values and canonical document |
| `VocabularyLifecycleModels.swift` | `VocabularyLifecycleModels.cs`: candidate/accepted/rejected interpretations and personal collection state |
| `VocabularySemanticModels.swift` | `VocabularySemanticModels.cs`: independent node/label/pattern/mapping/relation/cue/provenance values retained through projection |
| `Lingrove/Models/LanguageLearning/LearningExplanationModels.swift` | `VocabularyExplanationModels.cs`: saved explanation input/record with optional study language and unknown completeness |
| `Lingrove/Support/Vocabulary/VocabularyNotebookProjection.swift` | `VocabularyNotebookProjection.cs`: read joins, explicit lexical replacement, evidence forking and collection selection |
| `VocabularyLifecyclePolicy.swift` | `VocabularyLifecyclePolicy.cs`: resource reference/ownership admission, using the shared composed-range and explanation-result predicates |

## Ownership and version boundary

The `Resources` namespace represents Mac's version-seven canonical graph. Its immutable document
owns catalog, sources, occurrences, interpretations, collections, review templates and semantic
values. `Entries` joins all accepted sense-linked interpretations; `CollectedEntries` also requires
personal membership and includes only the collection's selected interpretation IDs. Unknown source
language stays null, and unknown context completeness remains distinct from fragment. Manual senses
may support general explanation without claiming the unique context-free favorite slot.

Catalog records own lexical knowledge. Sources and occurrences retain original evidence. Claims
own contextual interpretations and annotations. Collections own personal notes, tags, rich content
and reviews. Removing membership does not delete the other records. Semantic labels have IDs,
language and preference, not acceptance status; semantic claims retain separate provenance.

All lists are copied on construction/replacement, and equality/hash preserve sequence, optional
absence and canonical text equality. Pure projection methods return replacement snapshots rather
than mutating a borrowed UI value. Edited evidence forks source/occurrence identity while retaining
the interpretation ID and other claims. Translation-only changes retain source/occurrence identity.
Lexical replacement and uncollecting remain distinct operations: removing a lexical destination
detaches surviving claims as candidates, while removing a collection preserves knowledge/evidence.

The older `Core/Vocabulary` entry-owned document and its version-three codecs remain an explicit
compatibility boundary. They cannot represent nullable source language, independent evidence,
unresolved claims or collection membership, and must not be relabeled version seven. The new CLR
types prevent accidentally passing this graph to the old SQL/backup writer. This slice constructs
no second Store, archive, service, inference runtime or background task. The app-owned durable
upgrade/activation is the next boundary; this slice does not change user data or storage destinations.

## Admission and evidence

Lifecycle admission checks unique resource keys, valid source spans, optional sense references,
one accepted claim per occurrence, collection-selected accepted ownership, and the unique no-context
favorite slot per headword. Raw evidence without claims and claims without collection membership
remain valid. Canonical catalog senses must not contain joined personal state. Explanation input
validation preserves the current nullable-language/unknown-boundary shape; unchanged result and
text-span validation reuse existing pure helpers.

This is not whole-archive validation. Global lexical/alias identities, semantic references/cycles,
review/template validation, media ownership, byte codecs, SQL relationships and transaction/CAS
remain additional gates. A complete CLR representability pass (including invalid enum casts and
malformed nullable members across the graph) is required before a decoder or persistence adapter
admits arbitrary input; the lifecycle gate's local checks are not that full pass. The semantic declarations retain all source fields but do not yet implement
the full semantic validation/reconciliation or classification UI. `CONTEXT.md` defines extensible
categories/frameworks/membership; the latest Mac code has no category resource declarations yet,
so this port does not invent them.

Six grouped tests adapt the source lifecycle cases for independent unresolved evidence, collection
remove/recollect, correction/translation identity, invalid references/acceptance, unknown metadata,
precise timestamps and detached snapshot values. A focused lexical-delete demotion case follows the
actual projection rule, for which the Mac suite has no direct test. Source evidence is
`LingroveTests/Services/Vocabulary/VocabularyLifecycleTests.swift`, `VocabularyUnknownLanguageTests.swift`
and the manual/import general-explanation cases. These are pure Windows tests; the original Swift
app/test suites, SQL reopen/backup and live notebook UI are not executed by this slice.

Focused validation passes six tests; full Core4610 and portable App1222 tests pass with no failure
or skip. The Release Windows-targeting solution builds with zero warnings/errors. Independent
read-only source review checked the value/projection/lifecycle boundary and documented the separate
pre-activation codec/representability gates. Final-head CI is required before merge. Archive activation/migration,
creation/import commit, meanings UI and latest explanation-provider integration remain work.
Historical fixtures/source pins remain unchanged. No installer, release, model download, network
transmission or Mac worktree edit is added.
