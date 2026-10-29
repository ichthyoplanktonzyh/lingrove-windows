# Lingrove Windows Contributor Instructions

## Product scope

Lingrove has two equally core product capabilities: local-first autocomplete while writing and
language learning while writing. Treat both as first-class product work. The Windows code contains
the inherited autocomplete port and a scoped manual, read-only sentence-translation preview adapted
from Mac. Broader learning remains migration work. Do not describe planned or native-unverified
behavior as shipped or verified; see `docs/LEARNING_PREVIEW.md` for the exact implemented subset.

This repository is the Windows port of the macOS Lingrove project and retains its AGPL-3.0 license
and upstream attribution. During the migration, keep existing `Cotabby` namespaces, assemblies,
binary names, settings locations, and storage identifiers stable unless a specific migration is
designed and documented. Lingrove branding in documentation does not mean the Windows runtime has
been rebranded or that its data is compatible with the macOS app.

The maintainer clarified on 2026-10-29 that old-data compatibility is outside this campaign.
Do not implement legacy Windows conversion or cross-OS binary admission as a prerequisite to
current product work. Use the frozen canonical model in a fresh, explicitly versioned notebook
file under the existing Cotabby data root; preserve old files without reading, converting or
overwriting them. This does not waive current-format validation, stable record identity,
acknowledged transactions, media integrity, privacy, or the single production Store owner.

The [current-format production composition](docs/VOCABULARY_CANONICAL_APP.md) selects one canonical
Store for writing favorites, saved explanations and the notebook's scoped browsing/create/import
flow. Older editor/media/review/export library checks do not establish those operations in the new
production browser. Preserve the explicit unsupported states until each canonical module is ported.

## Migration principle: adapt the frozen macOS product

**本轮 Windows 版本以固定的 macOS 稳定快照为基准，对齐功能行为、信息架构（IA）、内容层级与状态表达；以美观、协调、易用为目标，允许保留 Windows/WPF 的设计特色，无需像素级复刻 Mac；不重新发明产品或业务架构。**

- Treat the frozen macOS implementation and its tests as the behavior and design reference. Read the relevant
  Mac subsystem before implementing its Windows counterpart; preserve its responsibilities, data
  contracts, defaults, user flow, prompting, budgets, cancellation, and privacy semantics.
- Port deterministic rules and regression cases directly into C# where possible. Do not invent a
  simpler replacement rule, change expected results to fit the Windows fork, or redesign a workflow
  merely because the implementation language or UI framework differs.
- Adapt the platform-dependent implementation: Accessibility/AppKit/SwiftUI become Windows UIA,
  Win32 and WPF, and unavailable Apple-specific providers require a Windows-supported counterpart.
  Keep the Mac module's responsibility and observable behavior wherever the platform permits.
- Record each migrated module's Mac source/test paths and reference revision, its Windows mapping,
  necessary platform differences, and remaining verification in the parity or stage documentation.
  A platform limitation is an explicit gap, not permission to silently change the product contract.
- Use architecture/design skills to understand the existing Mac design and place necessary Windows
  adapters. Their use does not authorize a new product design or a replacement business architecture.
  Product redesign belongs to separately scoped work, outside ordinary porting.
- A round may implement a documented subset of the Mac feature. Keep the remaining behavior visible
  as incomplete; do not substitute fake production output or describe the subset as full parity.

## Before editing

- Read this file and the relevant subsystem documentation before changing behavior.
- Check `git status -sb` and inspect the exact files you plan to touch. Preserve concurrent and
  user-authored changes.
- Keep changes within the requested scope. Coordinate before editing a shared file another worker
  owns.
- The maintainer froze this alignment campaign at Mac
  `35b7fbe5c788c5524936ade1dfb7c89a3a285b04` on 2026-10-29; see
  [the frozen baseline and backlog](docs/MAC_LATEST_BASELINE.md) and
  [the team and UI acceptance contract](docs/WINDOWS_ALIGNMENT.md). Read that exact committed
  snapshot for every new module. Do not chase or incorporate subsequent Mac commits unless the
  maintainer explicitly advances the campaign baseline.
- Existing modules/fixtures retain their explicit reference until upgraded individually, including
  the original `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` and intermediate
  `cc468b89f5c360cc7210fac4f1a8b52197870bf2`. Their passing checks do not establish latest parity.
  Never silently change a shared pin or fit old expectations to a new rule. Read committed evidence
  with `git -C <macOS-repository> show <commit>:<path>` rather than a dirty working tree. Preserve
  Mac work in progress; do not commit, push or alter it as part of the Windows adaptation.

## Architecture and ownership

Read [ARCHITECTURE.md](ARCHITECTURE.md) before moving responsibilities between layers and
[docs/PARITY.md](docs/PARITY.md) before claiming a feature is complete.

- `src/Cotabby.App/` owns WPF presentation, tray lifetime, settings views, and the application
  composition root (`Hosting/AppHost.cs`). Views express user intent and observe shared services;
  they should not construct duplicate focus, input, or inference services.
- `src/Cotabby.Core/` owns portable values and deterministic policies. Keep platform calls out of
  this project so the same behavior can run against shared JSON fixtures on CI.
- `src/Cotabby.Inference/` owns model and endpoint boundaries. Keep mutable native model state
  serialized, cancellable, and independent per task: autocomplete, learning, and speech must not
  borrow one another's live runtime or prompt/session state.
- `src/Cotabby.Win32/` owns Windows UI Automation, global input, insertion, clipboard, credential,
  power, and other OS effects. Keep Windows quirks here and feed the rest of the app immutable
  snapshots through narrow Core contracts.
- `tests/` mirrors production responsibilities. Prefer pure policy tests and shared fixtures before
  adding UI or native-runtime coupling.

Explain subsystem ownership and data flow in type/file comments when introducing a non-obvious
boundary. Put a rule in Core when it is deterministic; put effects in Win32 or Inference; keep App
coordinators focused on sequencing, cancellation, and current UI state.

## UI, interaction and visual parity

- Functional parity alone does not complete a user-facing module. Preserve the frozen Mac behavior,
  information architecture, navigation, content hierarchy, operation discoverability and
  loading/empty/error/disabled/selected state expression alongside its behavior.
- The maintainer clarified on 2026-10-29 that Windows need not visually duplicate Mac. Use attractive,
  coherent Windows/WPF controls, typography, icons, spacing, colors and window chrome while preserving
  the product's information hierarchy and observable behavior. Pixel equality is not an acceptance gate.
- Preserve keyboard interaction, focus/activation, dismissal, resizing and accessibility semantics.
  WPF is the rendering adapter; an existing Windows placeholder is not the design authority.
- Trace behavior, IA and state decisions to committed Mac views, resources and UI tests. Review
  rendered Windows views/screenshots for clarity, consistency and beauty, using available Mac images
  to compare hierarchy and state expression. Record missing references without claiming a comparison.
- Document Windows design choices, fonts, DPI and platform input conventions. Windows visual adaptation
  is authorized; changes to product flows or business architecture require separate scope.
- A compiled XAML/control test establishes component behavior, not physical desktop or visual
  acceptance. Record the executed checks and remaining native checks in the module handoff.

The [latest autocomplete output module](docs/AUTOCOMPLETE_OUTPUT_CURRENT.md) explicitly upgrades
normalization to the latest recorded Mac target. Preserve newline evidence through prompt/prefix
cleanup and decide spacing only at the surviving seam; the old shared corpus retains its own pin.

## Safety and privacy invariants

- Local inference is the default. No text, clipboard contents, screenshot, OCR, or notebook data may
  be sent to a service unless the user explicitly selects that task's endpoint and sees its
  destination. Endpoint settings are per task, and each request is bounded before transmission.
- Do not add cloud sync. The Windows port's local data stays local unless a distinct, explicitly
  authorized feature defines otherwise.
- Observe global keyboard input without taking focus. Suppress only a key the app has decided to
  consume. Treat IME preedit/composition as uncommitted input and do not translate, correct, or accept
  text against it.
- UI Automation is asynchronous and host-specific. Carry field identity, caret/selection state, and
  a freshness/version signal through asynchronous work; re-check them before showing or inserting a
  result. Fail closed for secure fields, unsupported controls, stale captures, and uncertain edits.
- Clipboard paste is an OS side effect. Preserve all available clipboard formats and handle a
  concurrent user copy safely; never silently claim rich clipboard restoration if only text was
  saved. A failed/uncertain replacement must not be retried against a different field.
- Bound and sanitize field, clipboard, OCR, dictionary, endpoint, and logging inputs. Normal logs
  must not contain typed text, prompts, selections, or credentials.

## Vocabulary and learning data

The [historical v7 resource value/projection boundary](docs/VOCABULARY_RESOURCES_CURRENT.md) lives
under `Core/Vocabulary/Resources` at its documented reference. It follows the catalog/evidence/
interpretation/collection ownership from that snapshot; it is not the complete frozen v8 boundary.
Its version-seven values are distinct from the older entry-owned runtime document.
Do not feed them through the old SQL/backup codec or claim archive activation/migration is complete.
Collection membership determines notebook display; raw evidence is not garbage when uncollected.

Preserve the macOS vocabulary contract when implementing learning storage: entry UUIDs identify
headwords, sense UUIDs identify meanings independently of their wording or order, and usage/example
UUIDs retain a particular encountered sentence or fragment with its exact text ranges and provenance.
Anki review/card identity is an independent relationship to those records. The [sense-content library](docs/VOCABULARY_CONTENT.md)
provides pure block/image rules; it is not a durable store. The [usage/capture values and keys](docs/VOCABULARY_CAPTURE.md)
preserve encountered text/ranges and provenance without connecting Save. The [notebook/review model graph](docs/VOCABULARY_MODELS.md)
now preserves absent content/cards/templates and independent review/binding identities. It defines
no persistence codec. The [save/sense rules](docs/VOCABULARY_SENSE_RULES.md) now return immutable
uncommitted drafts. The [archive validator](docs/VOCABULARY_ARCHIVE_VALIDATION.md) now supplies
pure whole-graph admission. [Archive draft edits](docs/VOCABULARY_ARCHIVE_DRAFTS.md) now retain
concurrent captures and prune stale explanations before semantic admission. [Relational projection](docs/VOCABULARY_RELATIONAL_PROJECTION.md)
now separates typed entity headers and relationships. The [row JSON codec](docs/VOCABULARY_PAYLOAD_CODEC.md)
preserves source leaf shapes. [SQLite transactions](docs/VOCABULARY_SQLITE_TRANSACTIONS.md) now run
against isolated in-memory databases. The [Windows file adapter](docs/VOCABULARY_WINDOWS_FILES.md)
adds named-file identity, locks and private ACL admission. The [serialized archive owner](docs/VOCABULARY_ARCHIVE.md)
now joins graph validation and revision acknowledgement. The [shared Store](docs/VOCABULARY_STORE.md)
is owned once by AppHost and restores/persists saved explanations. The [writing capture factory](docs/VOCABULARY_CARD_CAPTURE.md)
now preserves full example translations/provenance. [Writing-card favorites](docs/VOCABULARY_FAVORITES.md)
connect Save and exact Remove to the shared Store. The [notebook browser](docs/VOCABULARY_NOTEBOOK.md)
now connects stable sense navigation and list/detail browsing. The [current stable sidebar](docs/VOCABULARY_NOTEBOOK_CURRENT.md)
adds flat singleton selection, multi-sense header interaction and persisted list density. The [entry editor](docs/VOCABULARY_EDITOR.md)
adds isolated drafts and acknowledged Save/Cancel. [Current editor annotations](docs/VOCABULARY_EDITOR_CURRENT.md)
now preserve authored fields and re-anchor only ranges at the explicit current-stable reference. [Sense/usage actions](docs/VOCABULARY_ACTIONS.md)
now connect acknowledged New/Merge/Delete/Move. The [notebook session library](docs/VOCABULARY_NOTEBOOK_SESSION.md)
adds idle per-usage explanation ownership and explicit dictionary lifetime. [Notebook explanation rows](docs/VOCABULARY_EXPLANATION_UI.md)
now connect explicit generation and acknowledged adoption; the [dictionary disclosure](docs/VOCABULARY_DICTIONARY_UI.md)
connects explicit bundled lookup and word navigation. The [image preparation library](docs/VOCABULARY_IMAGE_PREPARATION.md) now supplies bounded PNG drafts;
[image storage library](docs/VOCABULARY_IMAGE_STORAGE.md) adds validated private media files and reuse/repair;
[archive media transactions](docs/VOCABULARY_MEDIA_TRANSACTIONS.md) now join staging, SQL and cleanup.
The [image UI](docs/VOCABULARY_IMAGE_UI.md) now connects file import, draft previews, saved-image reading and enlargement.
The [existing-file drop adapter](docs/VOCABULARY_FILE_DROP.md) adds a narrow Unicode Shell path;
the [image clipboard transfer library](docs/VOCABULARY_CLIPBOARD_TRANSFER.md) isolates bounded encoded-image
reads in a cancellable child process. [Image paste](docs/VOCABULARY_IMAGE_PASTE.md) now connects its
shared owner to the editor button and explanation-section command. The [DIB adapter](docs/VOCABULARY_CLIPBOARD_DIB.md)
adds bounded Windows bitmap envelopes to that same path. The [export projection library](docs/VOCABULARY_EXPORT_PROJECTION.md)
now preserves frozen scope IDs and portable learning-material fields. The [export text boundaries](docs/VOCABULARY_EXPORT_TEXT.md)
adapt CSV-cell escaping and byte-bounded preview. [TXT/CSV document rendering](docs/VOCABULARY_EXPORT_NOTES.md)
now adapts complete text traversal and ordered rows. [Portable JSON encoding](docs/VOCABULARY_EXPORT_JSON.md)
now preserves the original export bytes. The [prepared export package](docs/VOCABULARY_EXPORT_PACKAGE.md)
now captures bytes, preview, counts and ownership. [Archive export snapshots](docs/VOCABULARY_EXPORT_SNAPSHOT.md)
now collect exact senses and media under the shared storage span. The [stored ZIP encoder](docs/VOCABULARY_EXPORT_ZIP.md)
now preserves the original archive bytes and bounded path grammar. [Atomic export saving](docs/VOCABULARY_EXPORT_SAVE.md)
now protects storage destinations and publishes captured bytes. The [export session](docs/VOCABULARY_EXPORT_SESSION.md)
now preserves Store lifetime, stale-preview and save-identity guards. The [export window](docs/VOCABULARY_EXPORT_WINDOW.md)
connects frozen notebook offers, persistent batch IDs, controls and the native destination adapter;
the [full backup codec](docs/VOCABULARY_BACKUP_CODEC.md) now preserves the complete portable graph
and base64 media envelope. The [archive backup](docs/VOCABULARY_BACKUP_ARCHIVE.md) now freezes all
referenced media and atomically publishes the full notebook. The [restore transaction](docs/VOCABULARY_RESTORE.md)
now validates complete packages before staged replacement/rollback. The [Store backup/restore owner](docs/VOCABULARY_BACKUP_STORE.md)
now preserves shared-error and restored-media publication. The [backup/restore panels](docs/VOCABULARY_BACKUP_DIALOGS.md)
now adapt native Save/Open startup and ownership. The [notebook backup/restore UI](docs/VOCABULARY_BACKUP_UI.md)
now connects the commands, explicit confirmation and accepted-operation drain; physical Shell interaction remains work;
The [graph value library](docs/VOCABULARY_GRAPH_VALUES.md) now preserves identity/projection/settings/camera;
the [graph solver](docs/VOCABULARY_GRAPH_LAYOUT.md) now adapts the original Grape particle engine;
the [serialized graph worker](docs/VOCABULARY_GRAPH_WORKER.md) now preserves epoch/cache/layout lifetime;
the [graph preference/presentation library](docs/VOCABULARY_GRAPH_PRESENTATION.md) now preserves Codable settings and window-local state;
the [compiled graph canvas component](docs/VOCABULARY_GRAPH_CANVAS.md) now adapts drawing/input/lifecycle/virtual peers;
the [notebook graph integration](docs/VOCABULARY_GRAPH_NOTEBOOK.md) now connects original mode/chrome/settings and shared Store/detail selection;
the [review card material](docs/REVIEW_CARD_MATERIAL.md) now resolves original card sources, layouts, static previews and JSON values;
the [review preference boundary](docs/ANKI_REVIEW_SETTINGS.md) now preserves original settings, mapping bytes and explicit credential reads;
the [Windows Anki credential adapter](docs/ANKI_CREDENTIAL_STORAGE.md) now supplies exact-key current-user DPAPI storage independently of inference;
the [review controller](docs/ANKI_REVIEW_CONTROLLER.md) now adapts operation scopes, probes, status, navigation and protected template upgrades;
the [review/template transactions](docs/VOCABULARY_REVIEW_TRANSACTIONS.md) now preserve card CAS, merged-sense receipts and acknowledged Store publication;
the [review settings adapter](docs/ANKI_REVIEW_SETTINGS_UI.md) now connects shared ownership, explicit connection controls and normal-quit drain;
the [PCM foundation](docs/SPEECH_PCM_FOUNDATION.md) now supplies bounded Float32 blocks and queue accounting without a player/provider;
the [frame decoder](docs/SPEECH_FRAME_DECODER.md) now admits bounded fragmented helper frames without a subprocess;
the [export buffer](docs/SPEECH_EXPORT_BUFFER.md) now serializes request-owned audio accumulation/packaging without generation/playback;
the [provider metadata rules](docs/SPEECH_PROVIDER_METADATA.md) preserve exact Qwen companions, local languages and voice-tag compatibility without catalog selection;
the [speech language resolver](docs/SPEECH_LANGUAGE_RESOLUTION.md) preserves bounded phrase/context evidence and fallback without a native recognizer;
the [voice selector](docs/SPEECH_VOICE_SELECTION.md) preserves ranking and compatible preference with required platform collation, without an installed catalog;
the [recording preparation library](docs/REVIEW_AUDIO_PREPARATION.md) preserves sheet-local cache, busy/cancellation and borrowed speech contracts without a sheet/provider;
the [review session library](docs/ANKI_REVIEW_SESSION.md) preserves draft/CAS, guarded sends, conflicts and media sequencing without a preparation window;
the [serial batch library](docs/ANKI_REVIEW_BATCH.md) adds frozen rows, per-item retries/options and cancellation through the original single-session transaction;
the [speech preferences](docs/SPEECH_SETTINGS.md) retain original destination profiles, explicit credential access and playback invalidation without activating a provider;
the [speech observer and shared history](docs/SPEECH_TELEMETRY.md) preserve bounded correlation, runtime admission and opt-in metrics without activating a helper;
the [PCM facade](docs/SPEECH_PCM_PLAYER.md) preserves readiness, queue/drain, stale callbacks and tracked shutdown without an audio-device implementation;
the [Windows tempo effect](docs/SPEECH_PCM_TEMPO.md) supplies continuous pitch-preserving Float32 processing and explicit EOF/output-tail ownership, without activating a device;
the [Windows PCM device](docs/SPEECH_PCM_DEVICE.md) supplies a serialized renderer, MTA WASAPI output and gap-aware clock accounting, with native hardware acceptance and AppHost activation still open;
the [encoded speech codec](docs/SPEECH_AUDIO_CODEC.md) supplies bounded in-memory WAV/MP3 decoding and joined asynchronous export-duration validation, with encoded playback and application integration still open;
the [shared speech controller](docs/SPEECH_CONTROLLER.md) preserves resolved requests, lazy catalog/model selection, playback/export exclusivity and joined task ownership without native providers or AppHost activation;
the [independent speech HTTP adapter](docs/SPEECH_ENDPOINT.md) preserves bounded request-owned transport, compression, deadlines and redirect/cookie refusal without application activation;
the [local speech runtime](docs/SPEECH_LOCAL_RUNTIME.md) adapts lazy helper ownership, shared reaping and bounded PCM exchange, with Windows process/job/pressure adapters and no application activation;
the [native speech helper](docs/SPEECH_NATIVE_HELPER.md) preserves the full original generation loop and exact independent dependency overlay behind Windows effects; CPU build checks do not activate the application or establish model-backed playback;
compressed/profiled bitmap variants, virtual/pixel drops, review navigation/preparation/batch UI and other dictionary providers remain work. Do not flatten senses into
one translation string, regenerate stable IDs during edits, or treat a generated explanation as the
user's authored meaning. See the vocabulary models and porting gates in the linked docs. Storage
compatibility with older Windows or macOS data is out of scope under the maintainer's current
instruction. Any future conversion requires a separately scoped decision; current-format integrity
and ownership remain required.

## Build and CI boundary

For the physical Windows x64 development host, follow [the handoff guide](docs/WINDOWS_DEVELOPMENT.md).
Use `scripts/check-windows-dev.ps1` for read-only prerequisites. Actual host acceptance is distinct
from CI; preserve the latest source reference and do not treat the new resource value library as an
already activated archive. The maintainer now has an Intel/AMD x64 host available for development.

`global.json` pins .NET SDK `10.0.402`. The current developer checkout keeps that SDK in the ignored
repo-local `.dotnet/` directory. On macOS, use it to run portable tests and the committed Swift
behavior verifier:

```sh
export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
python3 scripts/verify-speech-controller-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-endpoint-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-runtime-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-learning-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-learning-local-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-learning-lexical-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-learning-explanation-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-learning-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-content-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-models-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-sense-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-validation-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-edit-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-projection-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-codec-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-sqlite-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-archive-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-store-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-card-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-notebook-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-editor-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-editor-current-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-notebook-session-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-image-preparation-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-image-storage-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-media-transactions-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-projection-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-text-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-notes-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-json-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-package-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-snapshot-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-zip-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-save-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-session-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-export-offer-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-backup-codec-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-backup-archive-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-restore-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-backup-store-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-graph-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-graph-layout-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-graph-worker-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-graph-presentation-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-review-card-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-anki-connect-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-anki-fields-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-text-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-export-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-anki-settings-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-anki-controller-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-vocabulary-review-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-anki-settings-interaction-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-pcm-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-frame-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-buffer-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-metadata-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-language-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-speech-voices-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
python3 scripts/verify-review-audio-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

The verifiers read the pinned macOS sources from Git and check the same JSON cases. The lexical
verifier also checks the three copied resource files and the complete actual Mac Codable resource
roundtrip; none builds the macOS app or runs a model. If `.dotnet/` is missing in a fresh checkout, install SDK
`10.0.402` into that ignored directory so `global.json` can select it.
The Windows-targeting build can compile the full solution, including WPF, from macOS. It does not
run the Windows test assembly or validate a Windows desktop; execute those tests on the Windows CI
runner. This is ordinary compilation, not packaging or a release.

The current `.github/workflows/ci.yml` has four validation jobs: Windows x64 restores and builds the
full solution, then runs Core, portable App boundary and Inference tests, including noninteractive ICU sentence tests,
and the compiled learning-card WPF view suite on its own STA dispatcher (see `docs/WPF_VIEW_TESTS.md`);
macOS 26 x64 runs Core, portable App boundary tests and the committed Swift parity verifiers (currently 68) against a sparse checkout of the pinned Mac
Support, language-learning/vocabulary models/services, content-test provenance, language catalog and pinned expression resource files. The separate
`.github/workflows/upstream-baseline.yml` checks the original Windows fork at commit
`b1e4031c70aca1be21aae2c57b138fde5fc22bb1` with .NET 9 on manual dispatch. Its initial setup run is
recorded in the baseline note. The third ordinary job compiles the independently pinned native
speech helper on Windows and runs model-free Git/protocol/platform checks; it produces no package.
The fourth job independently reads the frozen vocabulary revision and executes four bounded Swift
probes for real SQLite schema/classification migration, UUID-only row payloads and portable document
Codable cases with typed timestamp bit patterns, plus relational rows and populated named SQLite
reopen/migration. Their Windows consumers run in the ordinary tests. These corpora compare logical
rows in each runtime; they do not establish direct cross-OS binary admission, complete malformed
graph coverage, media or application compatibility. The historical 68 verifiers retain their
original references and bodies.
Current editor verification uses a separate sparse checkout at its explicit newer revision.
The upstream baseline job does not gate ordinary PRs; its result describes that old snapshot,
not the current .NET 10 tree.

CI is for dependency restore, compiling, and tests. It must not download GGUF model weights, create
installers or other downloadable packages, upload build artifacts, publish packages, or create a
release for each commit. Do not add a model-backed or network-dependent CI test. Ordinary package
manager restore needed to compile is allowed. Do not run the `Cotabby.Tools.*Smoke` projects as
routine macOS validation; they may need a Windows desktop, interactive editor, or downloaded model.
Native Windows UI Automation, IME, caret, insertion, clipboard, and GPU behavior still requires a
Windows host even when portable tests pass.

Report exact validation commands and outcomes. A passing compile is not proof of native UI behavior,
and a passing `build-for-testing` equivalent is not the same as executing tests.

## Completing a development round

The maintainer has authorized merging each completed round into `main` after independent review and
the required checks pass. Create a scoped PR, resolve review findings, verify CI for its final head,
and merge it without asking for another approval. The primary agent coordinates shared contracts,
integration, independent review and acceptance. The maintainer authorizes implementation delegates
to own complete modules, including production code, configuration, tests and module documentation.
Use `gpt-6.1-terra` with `high` reasoning for implementation and review delegates unless the
maintainer explicitly requests another setting. This supersedes the earlier root-only implementation
and Luna read-only delegation rule. Use one isolated worktree/branch per implementation owner;
coordinate shared-file changes through the primary agent. An author cannot be the sole reviewer of
their own change. Follow [the ownership and delivery contract](docs/WINDOWS_ALIGNMENT.md).
Continue to exclude installers, downloadable build artifacts and releases from routine rounds. A virtual machine is not a prerequisite for portable
logic, WPF compile checks or fake-driven tests; native desktop behavior still needs a Windows host.
The maintainer has also authorized continuous progress through the documented migration slices:
after merging a validated checkpoint, continue the next applicable slice without waiting for a
checkpoint approval or a dedicated reporting turn. Stop for a user decision only when a material
scope choice or an external prerequisite prevents safe progress.
