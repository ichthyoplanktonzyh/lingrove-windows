# Current Mac alignment baseline

The maintainer subsequently authorized latest-Mac functionality and deferred physical acceptance
to one final round. Read-only fetch on 2026-10-29 resolved Mac main to
**`3b53402f2cbc196b0398d133177fd80006d165b2`**, committed at 23:18:41 +08:00.
This advances the earlier frozen **`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`** reference.
The current target defines behavior, interaction and UI/design for new work. It includes the earlier task-shaped
capture/resource work, readability/consolidated detail, and the new extensible meaning categories
with catalog, CLI and notebook navigation. Mac source lives under `Lingrove/` and tests under
`LingroveTests/`; old `Cotabby/` paths remain valid only at historical revisions.

Before 35b, the preceding target was `9290066ef7f20a50068ce820f477737fdfc2b8b0`; 35b added
one classification round. The earlier intermediate target remains
`cc468b89f5c360cc7210fac4f1a8b52197870bf2`, and the original fixture reference remains
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. Commit counts are not workload estimates.
Existing Windows modules are partial adaptations at their explicitly recorded revisions. Neither
passing old fixtures nor updating this document establishes latest product parity.

## Reference ownership and freeze

Read relevant source and tests at the exact recorded SHA with `git show`. The maintainer now permits
latest-Mac alignment; inspect and record later committed changes before adopting them.
The Windows read-only reference repository at `D:\GitHub\lingrove-mac-reference` has fetched the
exact commits without checking out or editing a Mac working tree. New verifiers establish
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

The 35b→3b advance contains four commits (`833f53b`, `799715d`, `7c4f87a`, `3b53402`).
Classification now carries examples, scope notes, source references and interface-language
localizations, with a larger attributed catalog and a new CC-BY-SA notice. Meaning creation/editing
uses one detached semantic/classification draft and one acknowledged atomic organization write.
Sidebar, hierarchical pickers, guidance and detail views changed together; graph projection and CLI
contracts also changed. SQL schema remains unchanged; the new guidance belongs in existing payloads.
This does not authorize legacy conversion or imply that earlier UI candidates already match 3b.
Review and LanguageLearning service/model/support paths have no direct changes across this range;
their owners record that comparison while preserving older fixture references.

The committed Mac `SOURCE_LAYOUT.md`, `ARCHITECTURE.md` and `CLI.md` are the responsibility maps.
The paths below identify actual source, not newly designed Windows features. The table preserves
the initial gap audit; its initial Windows gap descriptions are historical. Current delivery is:

- Main contains [task/model assignment](MODEL_MANAGEMENT_FROZEN.md),
  [recommended setup](RECOMMENDED_SETUP_FROZEN.md), the single current-format
  [canonical notebook owner](VOCABULARY_CANONICAL_APP.md), creation/import/favorites,
  [entry editing and file images](VOCABULARY_CANONICAL_ENTRY_EDITOR_UI.md),
  [sense commands](VOCABULARY_CANONICAL_SENSE_UI.md), and the limited
  [expression-assistance application](EXPRESSION_ASSIST_APPLICATION.md).
- Main also contains [semantic/classification transactions](VOCABULARY_CANONICAL_SEMANTICS_STORE.md),
  [canonical Anki sessions](ANKI_CANONICAL_REVIEW_SESSION.md), and
  [unknown-source translation planning](READING_TRANSLATION_UNKNOWN_SOURCE.md).
  Their subsequent production activation is recorded below.
- The [meaning/mapping editors](VOCABULARY_CANONICAL_MEANING_UI.md) merged in #133.
  Current category management, usage-pattern editing, independent Anki card preparation and batch
  review merged in #136. The canonical graph merged in #137. All four final-head checks passed
  for each; this does not establish physical desktop acceptance.
- #138 merged [image paste/drop/enlargement](VOCABULARY_CANONICAL_ENTRY_EDITOR_UI.md),
  [full backup/restore](VOCABULARY_CANONICAL_BACKUP.md), [learning-material export](VOCABULARY_CANONICAL_EXPORT.md)
  and [manual selected-text reading](READING_SELECTION_CHECKPOINT.md), after independent review
  and all four final-head checks.
- #139 merged [notebook explanations/dictionary](CANONICAL_NOTEBOOK_LEARNING.md),
  [speech providers/settings/cards](SPEECH_PRODUCTION_3B.md) and
  [review audio/templates](ANKI_CANONICAL_REVIEW_AUDIO.md). Its source and actual AppHost attachment
  were independently reviewed and all four final-head checks passed.
- #140 merged pointer/icon/context/restricted-OCR reading, the private CLI mailbox, launch-time
  interface languages and shared performance history. #141 preserved all 68 original Mac verifiers
  while scheduling at most two concurrently; all four final-head checks passed for both PRs.
- #142 merged independent explanation endpoints and settings, both
  learning roles' battery/AC profiles, local MDict sources and rich articles, isolated native image
  drops, current autocomplete streaming/prediction and onboarding task readiness. Module references,
  scoped limitations and validation are recorded in their handoffs. All four final-head CI jobs
  passed at `09f2e797d950f64283858197392ccc051f26b6f2` before merge as `210dabe`.
  Physical acceptance remains the maintainer's final unified round.
- #143 merged [automatic writing previews and sentence operations](WRITING_TRANSLATION_3B.md):
  compact/expanded controls, acknowledged replacement, recovery Copy, explicit budgeted draft
  references and evidence-backed previous-sentence display. All four final-head CI jobs passed at
  `d5f0e52a54aefad88a16965424e6de68d4e6eef4` before merge as `889196d`.
  Its initial caret-ending ASCII-source subset was subsequently upgraded in #146; physical
  editor acceptance remains separate from that source implementation.
- #144 merged [online and website dictionaries](DICTIONARY_WEBSITE_ONLINE_3B.md), connecting
  explicit online offers/retry and browser destinations across floating cards and notebook, with
  shared settings, bounded HTTP cancellation/drain and website editing. All four final-head checks
  passed at `926ceaf6d937c7d02968a3cfb79862e2c9543a4c` before merge as `ded4be4`.
- #145 merged the [Home/Welcome showcase](HOME_WELCOME_SHOWCASE_3B.md), connecting both production
  entries to the same seven-scene player, real cards/notebook/review and private sample owners.
  All four final-head checks passed at `1018a9e7d96dee042f7a2a759b6599e1db6e9cf2` before merge as
  `9ad2a88`. Physical acceptance remains separate.
- #146 merged [writing range replacement](WRITING_RANGE_REPLACEMENT_3B.md), adding exact native
  selected/middle/Unicode scopes, independent selection/value acknowledgements and Copy recovery.
  All four final-head checks passed at `8042fa87daabfb63f140fe148845c1403e0e8bba` before merge as
  `eb51556`. Unsupported providers and physical editor acceptance remain explicit.
- #147 merged [expression candidate tools](EXPRESSION_CANDIDATE_TOOLS_3B.md), connecting independent
  row speech and canonical favorites with stable generated context/provenance. All four final-head
  checks passed at `c243bb3441ecf1bb967c18f67f43b44ad4b0a247` before merge as `1569ac4`.
- #148 merged [explicit-region reading](READING_EXPLICIT_REGION_3B.md), removing the editable-field
  prerequisite for the screenshot command while retaining native security/composition admission,
  origin/window freshness, local OCR and existing reading/Store ownership. All four final-head checks
  passed at `d5bd6830eb99d3d6b08634013bd7eb48e7226dfb` before merge as `69f5a73`.
- The [literal Unicode trigger candidate](EXPRESSION_UNICODE_TRIGGER_3B.md) connects policy-admitted
  fullwidth/symbol markers to one-shot exact native range selection for acceptance and cancellation,
  and routes all production ASCII/shortcut writes through the same acknowledged native transaction;
  its CI and physical host acceptance remain separate gates.
- Remaining work includes pointer/selection visual OCR fallback, native automatic language
  confidence, Apple's unavailable system dictionary and the explicitly documented media/native gaps.
  Internal components are not evidence that those flows are complete. Existing older libraries
  retain explicit references and are reused only after checking the current consumer contract.

After the maintainer's delivery adjustment, implementation owners carry each active user flow
through its settings, entry and production adapters. Internal commits use targeted validation;
since the later 2026-10-30 instruction, the primary alone implements new changes. The maintainer
subsequently authorized fresh `gpt-6-luna`/max agents for independent review/testing only, replaced
between modules. Required CI gates each complete candidate. Physical host acceptance belongs
to the maintainer using the [Windows host checklist](WINDOWS_HOST_ACCEPTANCE.md).

The following source inventory and original gap wording record the initial latest-Mac audit.
Completed module handoffs above and in [PARITY.md](PARITY.md) supersede those historical gaps;
the inventory must not be read as today's implementation completion list.

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
The maintainer subsequently removed old-data compatibility from scope: current-format notebook
activation uses a fresh versioned file in the existing Cotabby data root. Legacy Windows
conversion and cross-OS binary admission are not blockers; older files remain untouched. Current
format validity, record identity, media integrity, acknowledged operations and one shared Store
remain required.
The maintainer's 2026-10-29 clarification preserves behavior, IA, content hierarchy and state
expression while allowing attractive Windows/WPF visual design; Mac pixel identity is not required.

## Original implementation order (historical)

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
5. Complete latest onboarding, localization and remaining runtime/provider/CLI integration. Speech
   production adapters and review audio now follow the completed card/review foundations; their
   known gaps stay recorded until their full user flows are connected and reviewed.

This order follows frozen Mac ownership and dependencies. It does not authorize a replacement
product design, new hosted transmission or automatic classification. The maintainer subsequently
ended implementation delegation: the primary now writes whole functional blocks alone, with the
subsequently authorized Luna/max independent review/testing only. Earlier implementation team
records are historical evidence, not a requirement to start another coding agent. Merge reviewed
final-head green checkpoints to `main`.
Ordinary rounds create no installer, downloadable package, release, model download or VM. Windows
UIA/IME/insertion, ghost pixels, actual Anki, installed voices/audio and hardware/model quality still
require native acceptance beyond compiled components and automated tests.
