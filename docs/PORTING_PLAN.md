# Windows Porting Plan

The goal is a Windows product that reproduces Lingrove's macOS behavior while respecting Windows
focus, input, storage, and runtime constraints. Autocomplete and language learning remain equal
product pillars. The first milestone is repository foundation: documentation, build/test-only CI,
and initial shared JSON behavior fixtures. It does not implement language learning.

## Phase 0: foundation and portable behavior contracts

**Scope:** settle repo guidance, pin the .NET 10 SDK, add CI that restores/builds/tests only, and add
small shared JSON fixtures for behavior that can run without a model or OS UI. Keep existing Cotabby
namespaces, binary, and storage identifiers during migration. Preserve AGPL and third-party notices.

**Gate:** CI builds the solution and executes the relevant unit tests on a Windows runner. Shared
fixtures cover the selected portable policies and have a clear owner/test mapping. The workflow does
not download GGUF weights, publish packages, make installers or downloadable archives, upload build
artifacts, or create releases. A green portable test job does not mark native focus or insertion
behavior verified.

## Phase 1: autocomplete behavior parity

Port portable rules from the pinned macOS commit before adding host-specific work: eligibility,
bounded request construction, prompt section budgeting, completion normalization, seam and repetition
guards, token-healing rules where applicable, and acceptance/session reconciliation. Keep the pure
logic in `Cotabby.Core`; compare C# output to shared input/expected-output fixtures from Swift behavior
where the contract is genuinely shared.

**Gate:** fixtures cover ordinary and mid-word continuations, Unicode and CJK segmentation, trailing
spaces and punctuation, type-through, partial/whole acceptance, stale work, and unsupported fields.
Fixtures use no network or model. Every listed feature has a parity row and evidence in
[PARITY.md](PARITY.md).

## Phase 2: Windows host safety and first cross-pillar vertical slice

The first implementation slice establishes guarded editor dispatch before adding learning UI. Its
portable policy, coordinator cancellation, inline query anchoring, and Windows clipboard/dispatch
boundaries are recorded in [EDITOR_SAFETY.md](EDITOR_SAFETY.md). This is preparation for this phase;
the native host gate and first learning flow remain pending.

Validate UIA capture, caret and selection behavior, field identity, stale-result rejection, global
hook ownership, focus preservation, IME composition, and insertion in real Windows applications.
Cover native edit controls, Notepad, browser contenteditables, Electron, and representative Office or
mail fields. Handle text after the caret without painting over host text. Verify per-monitor DPI and
multi-line geometry.

In this phase, build the first narrow end-to-end learning slice alongside autocomplete: in one host
field, accept an autocomplete suggestion, capture a committed sentence, show a translation preview
using a deterministic fake engine, and offer explicit replacement only after rechecking the captured
field and selection. A manually configured endpoint can support a separate host walkthrough; CI and
fixtures use only the deterministic fake. This gives both equally core capabilities a real Windows
path before broadening learning into dictionaries, notebooks, Anki, or speech.

Before language-learning replacement depends on insertion, repair the clipboard path so a paste
preserves available clipboard formats and does not overwrite a copy the user made concurrently.
Recheck the focused field and selection immediately before deletion or insertion; fail closed if the
host no longer matches the captured target.

**Gate:** record the Windows version, app/editor versions, host and DPI setup for each check. Compare
rendered ghost and host text, accept and compare the resulting text, verify a focus change during
generation is discarded, and verify secure fields and uncommitted IME composition do not run tasks.
Verify that sentence translation first appears as a preview and replacement occurs only after the
original field identity and selection are acknowledged. Automated unit tests can supplement these
checks but cannot replace them.

## Phase 3: independent runtime and task routing

Stabilize local autocomplete inference and explicitly configured endpoint generation on Windows.
Then define separate task-owned runtime, settings, cancellation, and telemetry boundaries for
learning and speech. Do not let model switching, cache eviction, or cancellation in one task disturb
another. Keep Apple Intelligence unavailable on Windows and select Windows-supported alternatives.

**Gate:** local inference works on native x64 CPU; Vulkan is separately tested on supported hardware
and remains optional. Endpoint requests are bounded, identify the selected task and destination,
exclude unselected context, redact logs, and do not follow redirects with credentials. CI uses fakes
and fixtures, never downloads model weights or contacts an endpoint.

## Phase 4: language-learning core loop

Port language and settings values, request policies, translation and explanation output rules,
offline lexical word/expression recognition, and safe text replacement. Then add field selection,
reading capture, explicitly invoked region capture and OCR, offline lexicon and system dictionary
lookup, followed by MDX/MDD dictionaries. Capture only committed text, keep scope bounded, and present
replacement as an acknowledged edit tied to the original field identity.

**Gate:** shared policy fixtures pass; Windows host tests cover selection changes, IME composition,
stale captures, secure fields, OCR cleanup/bounds, replacement acknowledgement and failed edits. The
learning local runtime and endpoint settings are independent of autocomplete and opt in per task.

## Phase 5: durable learning, speech, and developer surfaces

Port the local notebook and its rich content, images, graph relationships, export formats, and
AnkiConnect/review behavior. Add system speech, local speech runtime, and an explicitly selected
speech endpoint as distinct providers. Port history and its privacy/deletion controls, metrics,
vocabulary CLI, settings/search, and interface localization. Preserve user-authored data through
schema migrations and keep runtime identifiers stable unless a migration is explicitly designed.

**Gate:** entry/sense/usage UUIDs survive edits, merges, export/import, and restart; Anki review IDs
remain attached to the correct cards; image references survive export; deletion really removes data;
speech cancellation cannot affect another task; CLI request replay cannot duplicate writes. Local
storage stays local and no sync service is introduced.

## Platform and hardware constraints

The current maintainer machine is an Apple M1 Max Mac with 32 GB RAM and no Windows desktop. A hosted
Windows CI runner can prove compilation and portable tests, but cannot establish field behavior on a
user's Windows desktop. Native x64 Windows is the primary app and inference verification target. An
ARM Windows virtual machine on Apple silicon can expose ARM-specific compile/runtime problems, but it
does not verify the x64 app or x64 Vulkan backend. Arrange a native Windows x64 test host before
claiming any platform-host gate is complete.

## Out of scope

No cloud sync, hosted default generation, analytics, per-commit release, install package, downloadable
build artifact, or model-weight download in CI. A user-configured remote endpoint is a deliberate,
visible task setting and is separate from cloud synchronization. This plan preserves the macOS
feature scope without claiming shared storage or cross-platform data compatibility.
