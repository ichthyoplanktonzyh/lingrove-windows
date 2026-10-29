# Frozen Mac alignment baseline

The maintainer froze this campaign at **`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`** on
2026-10-29. This is the stable behavior, interaction and UI/design reference for Windows; later
Mac commits are outside scope until explicitly adopted. It includes the earlier task-shaped
capture/resource work, readability/consolidated detail, and the new extensible meaning categories
with catalog, CLI and notebook navigation. Mac source lives under `Lingrove/` and tests under
`LingroveTests/`; old `Cotabby/` paths remain valid only at historical revisions.

The immediately preceding target was `9290066ef7f20a50068ce820f477737fdfc2b8b0`; the frozen
snapshot adds one committed classification round. The earlier intermediate target remains
`cc468b89f5c360cc7210fac4f1a8b52197870bf2`, and the original fixture reference remains
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. Commit counts are not workload estimates.
Existing Windows modules are partial adaptations at their explicitly recorded revisions. Neither
passing old fixtures nor updating this document establishes latest product parity.

## Reference ownership and freeze

Read relevant source and tests at the exact frozen SHA with `git show`; do not refresh the target
from Mac `main` at each feature round. A future advancement requires an explicit maintainer decision.
The Windows read-only reference repository at `D:\GitHub\lingrove-mac-reference` has fetched the
exact frozen commit without checking out or editing a Mac working tree. New verifiers establish
reproducible source availability independently. Do not push Mac work as a side effect.

The UI work previously excluded from `06bf81fe` is committed in `fc8f5b02` and included. This
Windows reference check makes no claim about the current state of the maintainer's Mac working
tree. Preserve Mac work in progress: do not edit, revert, stage, commit or publish it. Existing
features and PR #108 retain their recorded module references until individually upgraded.

Preserve historical corpus inputs, expected values and hashes. An explicit module upgrade reads
the new implementation and relevant original regressions, records changed behavior and adds only
the meaningful evidence needed for that change. Do not bulk-repin old verifiers or rewrite their
expectations to make Windows pass. Test-count/granularity cleanup remains deferred until replication
is complete; repairing an actual build/CI blocker is still required.

## Frozen product contracts and Windows gaps

The committed Mac `SOURCE_LAYOUT.md`, `ARCHITECTURE.md` and `CLI.md` are the responsibility maps.
The paths below identify actual source, not newly designed Windows features.

| Area | Latest source responsibility and required Windows alignment |
| --- | --- |
| Independent learning resources | The [resource value/projection adaptation](VOCABULARY_RESOURCES_CURRENT.md) supplies the versioned Windows value boundary and lifecycle reference gate; archive activation/migration is still pending.  `Models/Vocabulary/VocabularyLifecycleModels.swift`, `VocabularyModels.swift`, `Support/Vocabulary/VocabularyNotebookProjection.swift`, `VocabularyLifecyclePolicy.swift`: canonical catalog, sources, occurrences, interpretations and personal collections are independent. `document.entries` is a joined editing adapter; the Store/notebook presents `collectedEntries`. Collection membership does not own or erase raw evidence. Windows still has the earlier entry-owned graph and needs an explicit durable adaptation before assuming the latest ownership. |
| Meaning organization | `VocabularySemanticModels.swift`, `VocabularySemanticPolicy.swift`, `VocabularySemanticDatabaseProjection.swift`, `VocabularySQLiteMeaningSchema.swift`: independent multilingual meaning nodes, usage patterns, mappings, evidence and typed relations. The frozen classification round adds `VocabularyClassificationModels.swift`, catalog/policy/projection/row helpers and category sidebar/editor/sheet views. Preserve node/label/category/framework/link IDs, multilingual preference, provenance, multi-parent membership, catalog versions and local overrides. Labels have no acceptance status; interpretations do. Do not substitute lexical similarity or add an internal classifier. |
| Durable format and media | The frozen Mac uses SQLite schema 5 and portable notebook format 8, including explicit v7 portable admission. Selective JSON may use format 5 when classification is included; earlier conditional formats remain distinct. Read `VocabularyArchive`, database/classification projection and schema, CLI/resource transactions, export/backup and media ownership together. Windows SQLite schema 2/portable document 3 are older contracts; the PR #108 v7 value library is not an activated archive. Both databases using application ID `LGVN` does not make them compatible: even `usages` has different ownership/meaning. Define and verify compatibility explicitly; never silently reinterpret or replace an existing Windows archive. |
| Creation and import | `VocabularyEntryInput.swift`, `VocabularyEntryInputPolicy.swift`, `VocabularyImportedExamplePolicy.swift`, `VocabularyWordListPolicy.swift`, archive entry-import extension and creation/import sheets provide manual entry creation and bounded CSV/TXT mappings, previews and acknowledged commits. Preserve example ownership, translation variants and latest-snapshot re-planning. Import also collects catalog knowledge that already exists and records selected interpretation IDs, making the lifecycle/projection work a prerequisite for faithful commit behavior. Windows has no corresponding complete creation/import flow. |
| CLI and capture | `Models/CLI/CLICaptureModels.swift`, `CLICaptureContract.swift`, `CLIResourceContract.swift`, `VocabularyCLICapturePlan.swift`, CLI resource plans/archive extensions and `CLI.md` define additive task-shaped capture, independent resources, CAS and idempotent receipts through the app-owned archive. Authorized agent assertions default to `origin=model`, `status=confirmed`; proposed status remains explicit. Capture collects material, while low-level resource writes do not implicitly collect it. `vocab list/get` reads collected material; `data` commands expose independent resources. The CLI performs no AI/Anki calls. Windows has no complete CLI/mailbox; adapt the actual current protocol rather than the old entry-only protocol. |
| Notebook and review UI | Latest notebook controls, meanings and collection projection supersede the old browser's full scope. The existing Windows list-density/singleton round is an intermediate improvement. Reuse the shared Store and existing review sessions, adapting the current `VocabularyReviewSection`, `VocabularyReviewSheet`, `VocabularyReviewLayoutEditor` and batch sheet after confirming their latest source contracts. Preparation, batch and conflict-choice presentation remain work. |
| Expression assistance | `App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`, `Models/ExpressionAssist/`, `Services/ExpressionAssist/`, related Support/UI files preserve the latest explicit writing assistance, window activation and notebook actions. This is a distinct current learning flow, not an alternate sentence-translation button. Windows needs its existing focus/insertion safeguards carried into the actual Mac flow. |
| Reading and OCR | Read current `LearningReadingCoordinator`, `LearningReadingSourceReader`, reading context/selection values and OCR/runtime boundaries together. The latest flow uses frozen OCR context, verified AX enrichment, optional source language and explicit completeness evidence. Older autocomplete OCR and a manual writing preview do not establish this reading product. |
| Autocomplete and model routing | The [latest output-cleanup adaptation](AUTOCOMPLETE_OUTPUT_CURRENT.md) preserves leading-newline word boundaries through prompt/prefix/label cleanup and retains preface filtering. Its own final-head validation is required. Task-specific local-model selection, assignment and runtime isolation still need the current contracts audited/adapted. Autocomplete remains equally core alongside learning. |
| First use, language and presentation | Recommended setup, expression-assistance onboarding, current language resources, settings grouping, meaning labels and native host presentation require adaptation. Namespace/release changes do not themselves migrate Windows binary/storage identifiers or authorize release packages. |

The frozen `CONTEXT.md` distinguishes meaning category, classification framework, custom category
and category membership from meaning nodes and expression senses. Categories may have multiple
parents and nodes may belong to multiple categories. The frozen implementation now supplies
independent framework/category/parent/membership/override records, a bundled versioned catalog,
bounded graph validation, durable schema evolution and notebook/CLI operations. Adapt those
committed contracts and tests; the older v7 Windows value boundary does not yet supply them.

Functional and UI/design scope, team ownership, shared-file coordination and rendered/native
acceptance are defined in [WINDOWS_ALIGNMENT.md](WINDOWS_ALIGNMENT.md).

## Next implementation order

1. Preserve the autocomplete newline/echo adaptation merged in #107. Complete frozen
   task-compatible model assignment in the existing model-management owners;
   multi-role recommended setup depends on it. These are adaptations of current Mac rules.
2. Reconcile the latest vocabulary ownership, value/projection and durable-format changes before
   extending the older notebook/review UI. Keep the existing shared archive/Store as the sole writer
   and preserve acknowledged identities, user material and media lifetime.
3. Connect latest manual creation/import and meaning organization to visible notebook operations,
   then finish review preparation, layout, batch and conflict choices over the same Store.
4. Adapt latest expression assistance and reading/OCR, carrying field/selection receipts,
   cancellation, bounded context and per-task destination disclosure through Windows APIs.
5. Complete latest onboarding, localization and remaining runtime/provider/CLI integration. Audio expansion remains
   deferred while these visible workflows are adapted; its known gaps stay recorded.

This order follows frozen Mac ownership and dependencies. It does not authorize a replacement
product design, new hosted transmission or automatic classification. The primary agent coordinates
module implementation delegates using GPT-6.1 Terra xhigh, shared contracts and independent review.
Merge reviewed final-head green checkpoints to `main`.
Ordinary rounds create no installer, downloadable package, release, model download or VM. Windows
UIA/IME/insertion, ghost pixels, actual Anki, installed voices/audio and hardware/model quality still
require native acceptance beyond compiled components and automated tests.
