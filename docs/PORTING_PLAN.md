# Windows Porting Plan

The goal is a Windows product that reproduces Lingrove's macOS behavior while respecting Windows
focus, input, storage, and runtime constraints. Autocomplete and language learning remain equal
product pillars. The first milestone is repository foundation: documentation, build/test-only CI,
and initial shared JSON behavior fixtures. It does not implement language learning.

## Governing rule: port, then adapt platform details

**基于已收敛的 macOS 成果做 Windows 适配，不重新发明产品、交互或业务架构。**

The pinned Mac implementation and tests define each feature's intended behavior. For every slice:

1. Read the corresponding Mac source and regression tests at the recorded stable revision.
2. Map its existing responsibilities and contracts to Windows files, preserving defaults, user
   flow, request scope, prompts, budgets, task isolation, cancellation and privacy rules.
3. Port deterministic logic and test cases before replacing AX/AppKit/SwiftUI or Apple-specific
   providers with the necessary UIA/Win32/WPF and Windows inference implementations.
4. Record source/test mappings, unavoidable platform differences and deferred functionality in
   [PARITY.md](PARITY.md) or the stage record. Keep unsupported behavior explicit rather than silently
   replacing it with a simpler product flow or adjusting expected tests to the current Windows code.
5. Validate the scoped port, review it independently, and merge the completed round to `main`.
   Architecture/design skills assist this adaptation; product redesign is outside the port's scope.

The phases below sequence migration of existing Mac capabilities. A phase can deliver a documented
subset, and passing its checks does not establish full feature parity. Fake engines are test inputs;
production uses a real configured provider or an explicit unavailable state.

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

This phase has incremental slices. The current learning slice ports the Mac manual sentence
preview: explicit trigger, committed sentence capture, task-owned translation and a read-only result.
It includes the minimum independent learning settings and task cancellation. Production uses a real
configured provider or reports that it is unavailable. Deterministic fake engines exercise the same
interfaces in CI and fixtures, with no network or models; they are not a production provider.

Follow-up slices add acknowledged replacement after rechecking the captured field/selection, and
an autocomplete-plus-learning walkthrough in one host field. A manually configured endpoint can
support that separate host walkthrough. Replacement, dictionaries, notebook, Anki and speech are
outside the current preview slice, while remaining migration targets for this phase and later ones.

Before language-learning replacement depends on insertion, repair the clipboard path so a paste
preserves available clipboard formats and does not overwrite a copy the user made concurrently.
Recheck the focused field and selection immediately before deletion or insertion; fail closed if the
host no longer matches the captured target.

**Current preview slice gate:** map the implementation and regression cases to the pinned Mac
source/tests, pass portable tests and the Windows compile/adapter checks, and record native capture,
IME and WPF interaction as unverified until a Windows host is available. This gate does not complete
the broader native-host or replacement milestone.

**Full phase gate:** record the Windows version, app/editor versions, host and DPI setup for each check. Compare
rendered ghost and host text, accept and compare the resulting text, verify a focus change during
generation is discarded, and verify secure fields and uncommitted IME composition do not run tasks.
Verify that sentence translation first appears as a preview and replacement occurs only after the
original field identity and selection are acknowledged. Automated unit tests can supplement these
checks but cannot replace them.

## Phase 3: independent runtime and task routing

Stabilize local autocomplete inference and explicitly configured endpoint generation on Windows.
Expand the minimum independent translation configuration and cancellation introduced by the preview
slice into the Mac-defined local runtime, task routing, model lifecycle and telemetry responsibilities
for learning and speech. Do not let model switching, cache eviction, or cancellation in one task
disturb another. Keep Apple Intelligence unavailable on Windows and select supported alternatives.

**Current implementation slice:** a dedicated CPU learning runtime, local/endpoint routing, exact
token planning, automatic/manual templates, explicit catalog downloads/imports and per-model
settings are mapped in [LEARNING_LOCAL_RUNTIME.md](LEARNING_LOCAL_RUNTIME.md). Portable and fake
tests establish selected policies and lifetime invariants. Release on disable/pause/provider changes
has policy and fake-driven drain/reload coverage. Native model execution and GPU routing remain
unverified; prewarm, idle eviction and power-specific profiles remain open.

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

Completed translations now support bundled word lookup. Explanation input, prompt budgeting and
complete-output parsing and model capability/metadata gates have [Mac-backed pure coverage](LEARNING_EXPLANATION_RULES.md).
The local explanation provider now joins that gate with actual token budgeting and complete-output
validation. A writing-only capture adapter now projects completed preview/unit/component targets
into frozen explanation inputs with exact original occurrences and context receipts. The library
service now owns saved-first lookup and the bounded complete-record cache. AppHost now composes
independent local runtime/settings with that service and its on-demand/60-second idle policy. The
portable session now preserves saved restoration, refresh and cancellation. The writing sentence
card now connects it with explicit controls and captured-document freshness checks; native desktop
and model execution remain unverified. The contextual query library now sequences unit/component
explanation with lazy dictionary navigation. Its WPF card now connects controls, exact examples
and captured-field guards; native interaction and selectable text remain unverified/unported. Original,
selected-text and reading captures, endpoint transport and durable records remain later work.

**Gate:** shared policy fixtures pass; Windows host tests cover selection changes, IME composition,
stale captures, secure fields, OCR cleanup/bounds, replacement acknowledgement and failed edits. The
learning local runtime and endpoint settings are independent of autocomplete and opt in per task.

## Phase 5: durable learning, speech, and developer surfaces

Port the local notebook and its rich content, images, graph relationships, export formats, and
AnkiConnect/review behavior. Add system speech, local speech runtime, and an explicitly selected
speech endpoint as distinct providers. Port history and its privacy/deletion controls, metrics,
vocabulary CLI, settings/search, and interface localization. Preserve user-authored data through
schema migrations and keep runtime identifiers stable unless a migration is explicitly designed.

**Current library slice:** [sense-content values/policy](VOCABULARY_CONTENT.md) ports ordered stable block IDs, text/image metadata budgets and exact review HTML using 72 actual pinned Swift observations. It does not connect a notebook, media decoder, database or Anki server. The [usage/capture library](VOCABULARY_CAPTURE.md) now also preserves stable usage values and keys with 63 actual Mac probes; entry/sense/review graphs and save/merge/storage remain next boundaries.

**Gate:** entry/sense/usage UUIDs survive edits, merges, export/import, and restart; Anki review IDs
remain attached to the correct cards; image references survive export; deletion really removes data;
speech cancellation cannot affect another task; CLI request replay cannot duplicate writes. Local
storage stays local and no sync service is introduced.

## Platform and hardware constraints

The current maintainer machine is an Apple M1 Max Mac with 32 GB RAM and no Windows desktop. A hosted
Windows CI runner can prove compilation, portable tests and isolated compiled WPF view behavior
(see [view tests](WPF_VIEW_TESTS.md)), but cannot establish field behavior on a
user's Windows desktop. Native x64 Windows is the primary app and inference verification target. An
ARM Windows virtual machine on Apple silicon can expose ARM-specific compile/runtime problems, but it
does not verify the x64 app or x64 Vulkan backend. Arrange a native Windows x64 test host before
claiming any platform-host gate is complete.

## Out of scope

No cloud sync, hosted default generation, analytics, per-commit release, install package, downloadable
build artifact, or model-weight download in CI. A user-configured remote endpoint is a deliberate,
visible task setting and is separate from cloud synchronization. This plan preserves the macOS
feature scope without claiming shared storage or cross-platform data compatibility.
