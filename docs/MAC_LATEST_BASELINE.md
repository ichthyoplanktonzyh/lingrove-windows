# Latest Mac alignment target

Windows follows the latest committed Mac implementation, rather than waiting to finish an older
stable snapshot. Current recorded target: **`06bf81fe52f22fd62e76d8ec0bfd898d3417158b`**, committed
on 2026-10-28 and checked on 2026-10-29. Its last change adds task-shaped vocabulary capture and
adopts authorized agent organization by default. Mac source now lives under `Lingrove/` and tests
under `LingroveTests/`; old `Cotabby/` paths remain valid only at historical revisions.

The previous target was `cc468b89f5c360cc7210fac4f1a8b52197870bf2`; the original fixture reference
is `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The latest target is 55 commits after the previous
one, including merge commits and namespace changes. That count is not a workload estimate.
Existing Windows modules are partial adaptations at their explicitly recorded revisions. Neither
passing old fixtures nor updating this document establishes latest product parity.

## Reference ownership and refresh

At the beginning of each new feature round, inspect the Mac repository's committed `main`, record
any newer target here, then read the relevant committed source and tests with `git show`. A local
commit is still product evidence even before publication: the current target is one commit ahead
of the last observed `origin/main`. Do not assume it is available to a remote CI checkout or push
Mac work as a side effect. A new source verifier must establish its own reproducible availability.

The Mac working tree has uncommitted UI changes. They remain owned by the Mac work and are excluded
from this immutable reference. Do not edit, revert, stage, commit or publish them. Incorporate their
next committed version when refreshing the target. A feature already under validation keeps its
recorded snapshot; the next feature checks for advancement again.

Preserve historical corpus inputs, expected values and hashes. An explicit module upgrade reads
the new implementation and relevant original regressions, records changed behavior and adds only
the meaningful evidence needed for that change. Do not bulk-repin old verifiers or rewrite their
expectations to make Windows pass. Test-count/granularity cleanup remains deferred until replication
is complete; repairing an actual build/CI blocker is still required.

## Latest product contracts and Windows gaps

The committed Mac `SOURCE_LAYOUT.md`, `ARCHITECTURE.md` and `CLI.md` are the responsibility maps.
The paths below identify actual source, not newly designed Windows features.

| Area | Latest source responsibility and required Windows alignment |
| --- | --- |
| Independent learning resources | `Models/Vocabulary/VocabularyLifecycleModels.swift`, `VocabularyModels.swift`, `Support/Vocabulary/VocabularyNotebookProjection.swift`, `VocabularyLifecyclePolicy.swift`: canonical catalog, sources, occurrences, interpretations and personal collections are independent. `document.entries` is a joined editing adapter; the Store/notebook presents `collectedEntries`. Collection membership does not own or erase raw evidence. Windows still has the earlier entry-owned graph and needs an explicit durable adaptation before assuming the latest ownership. |
| Meaning organization | `VocabularySemanticModels.swift`, `VocabularySemanticPolicy.swift`, `VocabularySemanticDatabaseProjection.swift`, `VocabularySQLiteMeaningSchema.swift`: independent multilingual meaning nodes, usage patterns, mappings, evidence and typed relations. `VocabularyMeaningBrowserProjection.swift`, `VocabularyMeaningLabelPolicy.swift` and the meaning/sidebar/detail/editing views expose the existing organization flow. Preserve node/label IDs, label language/preference, relation endpoints and provenance on mappings, relations, cues and targets. Labels have no acceptance status; interpretations do. Do not substitute lexical similarity or add an internal classifier. |
| Durable format and media | The latest Mac uses SQLite schema 4 and portable notebook format 7. Selective JSON uses format 4 only when meaning export is enabled, the export is not an expression list and semantic nodes or patterns exist; other configurations retain versions 1–3. Read `VocabularyArchive`, database projection, CLI/resource transactions, export/backup and media ownership together. Windows SQLite schema 2/portable document 3 are older contracts. Both databases using application ID `LGVN` does not make them compatible: even `usages` has different ownership/meaning. Define and verify compatibility explicitly; never silently reinterpret or replace an existing Windows archive. |
| Creation and import | `VocabularyEntryInput.swift`, `VocabularyEntryInputPolicy.swift`, `VocabularyImportedExamplePolicy.swift`, `VocabularyWordListPolicy.swift`, archive entry-import extension and creation/import sheets provide manual entry creation and bounded CSV/TXT mappings, previews and acknowledged commits. Preserve example ownership, translation variants and latest-snapshot re-planning. Import also collects catalog knowledge that already exists and records selected interpretation IDs, making the lifecycle/projection work a prerequisite for faithful commit behavior. Windows has no corresponding complete creation/import flow. |
| CLI and capture | `Models/CLI/CLICaptureModels.swift`, `CLICaptureContract.swift`, `CLIResourceContract.swift`, `VocabularyCLICapturePlan.swift`, CLI resource plans/archive extensions and `CLI.md` define additive task-shaped capture, independent resources, CAS and idempotent receipts through the app-owned archive. Authorized agent assertions default to `origin=model`, `status=confirmed`; proposed status remains explicit. Capture collects material, while low-level resource writes do not implicitly collect it. `vocab list/get` reads collected material; `data` commands expose independent resources. The CLI performs no AI/Anki calls. Windows has no complete CLI/mailbox; adapt the actual current protocol rather than the old entry-only protocol. |
| Notebook and review UI | Latest notebook controls, meanings and collection projection supersede the old browser's full scope. The existing Windows list-density/singleton round is an intermediate improvement. Reuse the shared Store and existing review sessions, adapting the current `VocabularyReviewSection`, `VocabularyReviewSheet`, `VocabularyReviewLayoutEditor` and batch sheet after confirming their latest source contracts. Preparation, batch and conflict-choice presentation remain work. |
| Expression assistance | `App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`, `Models/ExpressionAssist/`, `Services/ExpressionAssist/`, related Support/UI files preserve the latest explicit writing assistance, window activation and notebook actions. This is a distinct current learning flow, not an alternate sentence-translation button. Windows needs its existing focus/insertion safeguards carried into the actual Mac flow. |
| Reading and OCR | Read current `LearningReadingCoordinator`, `LearningReadingSourceReader`, reading context/selection values and OCR/runtime boundaries together. The latest flow uses frozen OCR context, verified AX enrichment, optional source language and explicit completeness evidence. Older autocomplete OCR and a manual writing preview do not establish this reading product. |
| Autocomplete and model routing | The [latest output-cleanup adaptation](AUTOCOMPLETE_OUTPUT_CURRENT.md) preserves leading-newline word boundaries through prompt/prefix/label cleanup and retains preface filtering. Its own final-head validation is required. Task-specific local-model selection, assignment and runtime isolation still need the current contracts audited/adapted. Autocomplete remains equally core alongside learning. |
| First use, language and presentation | Recommended setup, expression-assistance onboarding, current language resources, settings grouping, meaning labels and native host presentation require adaptation. Namespace/release changes do not themselves migrate Windows binary/storage identifiers or authorize release packages. |

## Next implementation order

1. Repair the identified latest autocomplete newline/echo boundary behavior in the existing
   normalizer. Track task-compatible model assignment in the existing model-management owners;
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

This order follows current Mac ownership and dependencies. It does not authorize a replacement
product design, new hosted transmission or automatic classification. Root writes code; GPT-6 Luna
max agents independently review and test. Merge reviewed final-head green checkpoints to `main`.
Ordinary rounds create no installer, downloadable package, release, model download or VM. Windows
UIA/IME/insertion, ghost pixels, actual Anki, installed voices/audio and hardware/model quality still
require native acceptance beyond compiled components and automated tests.
