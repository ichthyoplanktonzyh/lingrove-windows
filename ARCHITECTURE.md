# Lingrove for Windows Architecture

This is a maintainer map for the Windows port. The source and binaries still use Cotabby identifiers
during migration. The repository contains the inherited autocomplete implementation and a scoped
manual read-only translation preview. Other learning flows below remain migration targets; native
Windows host behavior is unverified. See [the preview port record](docs/LEARNING_PREVIEW.md).

## Architecture authority

The stable macOS Lingrove implementation is the architecture and behavior reference for this port.
The Windows work adapts the existing macOS work; it does not independently redesign Lingrove's product,
interaction model, or business rules. The pinned reference is
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`; compare committed source and tests rather than a dirty
working checkout. The project/layer map below translates existing responsibilities into C# and
Windows technology, and the future-flow descriptions identify migration targets rather than new
product proposals.

Preserve the Mac domain values, settings semantics, request scope, prompts and budgets, task/runtime
ownership, cancellation, stale-result checks, and privacy behavior. Port pure rules and their
regression cases first. Replace platform implementations where needed: AX observation with UIA,
AppKit/SwiftUI presentation with WPF/Win32, and Apple-only inference with a supported Windows
provider. A different framework does not by itself justify a different user flow.

Each migrated subsystem must trace its Windows files and tests to the Mac source/test paths at the
reference revision. Document unavoidable platform differences and unimplemented behavior alongside
the evidence in [PARITY.md](docs/PARITY.md) or the stage record. Architecture skills support reading
that existing design and implementing these adapters; they do not supply an alternative product
architecture. A fake engine belongs in tests, while the app must use a real configured provider or
report that the provider is unavailable.

## Product loops

Autocomplete and language learning are equally core product capabilities. Autocomplete observes an
editable field, forms a bounded request, gets a short continuation, paints a ghost beside the caret,
and inserts only the portion the user accepts. Language learning should capture a sentence, selection,
or explicitly selected screen region, translate or explain it, and let the user safely save or replace
text. Both loops share app lifetime, focus, input, and settings infrastructure, but each owns its own
task identity, cancellation, presentation state, and inference profile.

The default is local. A configured OpenAI-compatible endpoint is an explicit per-task choice: the
user must know which task can send a request and where it goes. Keep request contents bounded and
avoid sending surrounding text, clipboard data, OCR, or notebook records unless that context is both
enabled and included in the visible endpoint decision. Cloud synchronization is outside this port's
scope.

## Current project boundaries

| Project | Owns | Data flowing through it |
| --- | --- | --- |
| `Cotabby.App` | WPF tray app, settings and onboarding surfaces, process lifetime, dependency composition | User intents and immutable service state; it coordinates, but should not own OS policy or native pointers |
| `Cotabby.Core` | Portable request/value types, suggestion state machine, prompt/output policies, context filtering, emoji, macros, spelling rules | Immutable field snapshots and settings in; normalized requests/results and deterministic decisions out |
| `Cotabby.Inference` | Local llama.cpp/LLamaSharp and OpenAI-compatible inference transport | Bounded immutable requests in; cancellable partial/final generation results out |
| `Cotabby.Win32` | UI Automation, keyboard hook, input insertion, clipboard, DPAPI, Windows spell checking, power | OS observations become Core values; accepted Core actions become guarded OS effects |
| `Cotabby.ImageTransferHost` | Short-lived STA image clipboard extraction, terminated by its Win32 owner on cancellation | Captured numeric sequence in; bounded encoded-image bytes over stdout out; no WPF, model or network dependency |
| `tests/` | xUnit coverage for Core and inference contracts | Portable deterministic behavior; native app behavior requires a Windows host |

`AppHost` is the app's composition root. It creates long-lived settings, focus and keyboard services,
the local and endpoint engines, overlay presenter, and `SuggestionCoordinator`, then wires those
objects together. Keep process-wide listeners and mutable runtimes there instead of constructing
them from individual windows. `SuggestionCoordinator` sequences eligibility, debounce, work
cancellation, request creation, active session reconciliation, presentation, and acceptance.
`SuggestionRequestFactory`, `SuggestionSessionReconciler`, output policies, and the `ISuggestionEngine`
contract keep behavior independently testable.

## Autocomplete flow

```text
Windows focus event / UI Automation snapshot
  -> UiaFocusTracker -> immutable FocusedField
Low-level keyboard hook (observe without taking focus)
  -> SuggestionCoordinator
  -> eligibility + debounce + cancellation identity
  -> SuggestionRequestFactory
       -> bounded field text + opted-in app/clipboard context
  -> SuggestionEngineRouter
       -> local LLamaSharp runtime OR explicitly configured endpoint
  -> CompletionPipeline / normalizer / seam and repetition policies
  -> active SuggestionSession -> GhostOverlayWindow
  -> accept key -> current-field check -> ITextInserter
```

The request must be a snapshot; engines must not query a live UIA element while generating. A focus
change, selection movement, changed caret context, changed settings, cancellation, or newer request
makes older work stale. Check the field identity and freshness again before presentation and again
before insertion. A host may remove an inserted string from its accessibility tree only after an
eventual-consistency delay, so acceptance reconciliation should explicitly model that delay instead
of accepting against an unrelated field.

The current platform boundary is `IFocusTracker` and `ITextInserter` in Core, implemented by
`UiaFocusTracker` and Win32 insertion services. UI Automation patterns vary across native, browser,
Electron, and custom editors; a field that cannot provide enough text/caret/selection information is
not eligible for a speculative edit. Focus, selection and caret support must be tested per host.

The keyboard hook may suppress the accept key only after an active suggestion owns that key. It must
not steal focus. IME preedit text is not committed user text: learning capture, autocorrection,
completion acceptance, and replacement must wait until composition is committed or cancelled.
Carry that state as an explicit signal rather than guessing from keystroke timing.

`FocusedField` carries an explicit field ID, a positive focus epoch, and reliable selection evidence.
The UIA producer leaves unsupported selection state unknown and skips text reads for secure fields.
Core's `InsertionTargetPolicy` compares immutable snapshots; `GuardedTextInserter` serializes the
whole edit, revalidates after queueing and clipboard preparation, and checks cancellation before
dispatch. `PendingEditLifetime` revokes queued autocomplete/correction edits when focus, settings,
ordinary input, or dismissal invalidates their authorization. Macro and emoji replacement retain the
pre-trigger capture and require the exact typed query at its original caret.

`SendInputTextInserter` is the App-facing facade over this Core transaction. `Win32InsertionBackend`
owns fresh focus reads, native dispatch and clipboard leases. Short text uses Unicode input; a longer
or multiline edit stages paste before sending any deletion. Delete and insertion events share one
native batch, and partial `SendInput` results fail without retry. Until host range replacement is
validated, destructive spans are limited to printable ASCII; Unicode insertion remains supported.

The clipboard lease deep-copies all enumerated supported HGLOBAL formats before mutation, capped at
32 MiB and 512 formats. Supported formats include text, DIB, file-drop lists, HTML and RTF. Any
unsupported format (including GDI handles and unknown registered formats) or failed snapshot rejects
paste before the clipboard is emptied. Restoration checks both sequence and live owner HWND while
holding the clipboard open, so a concurrent user copy wins. `ClipboardWriteAccounting` subtracts only
the app's actual write deltas rather than hiding the entire lease interval.

These checks establish dispatch policy, not editor acknowledgement. The paste-consumption delay is
a heuristic, clipboard restoration after a native failure is best effort, and focus can still change
after the last OS read. Native host validation, IME composition and acknowledged replacement remain
open before language learning can depend on this path. See [the safety record](docs/EDITOR_SAFETY.md).

## Learning flow and independent runtimes

The original Windows baseline had no learning subsystem. The current manual-preview port supplies
learning contracts, request orchestration, a dedicated local runtime, a task-owned endpoint adapter
and a read-only WPF panel.
The preview supports bounded safe streaming prefixes and the Mac completed-result cache policy.
Enabled-source settings and deterministic confidence ranking are ported; a Windows statistical
recognizer is still unavailable, so multiple eligible sources fail closed rather than using
invented confidence. A single explicit source continues to work without recognition.
The wider Mac learning flow below remains the behavior reference for later Windows slices:

```text
committed typing / explicit selection / explicit region capture
  -> capture policy, current-field identity, IME and secure-field checks
  -> immutable, bounded LearningTranslationInput + captured task settings
  -> learning task router + its own local runtime or explicitly selected endpoint
  -> translation / explanation / offline lexical word-expression recognition / dictionary lookup
  -> result card with provenance and safe replacement acknowledgement
  -> optional save to local vocabulary store -> notebook / Anki export or connection
```

`LearningTranslationInput` is the Mac text payload contract. `LearningRequestContext` carries
request/operation correlation and engine metadata separately; it does not contain the source text.

The current Windows learning slice ports the Mac manual current-sentence preview: explicit trigger,
captured sentence, task-owned translation and a read-only result. Production uses a real configured
provider or reports that it is unavailable; deterministic fake engines exercise the same interfaces
only in tests. This slice establishes the minimum independent translation settings and cancellation.
Local request planning resolves actual model metadata, tokenizes trusted framing separately from
literal source, and validates the actual context allocation before cache lookup or generation. The
short-lived engine facade owns one exclusive operation lease; AppHost owns and drains the reusable
learning runtime and explicit model-file manager. The learning backend selector never falls back
from a local error to a network service. See [the runtime map](docs/LEARNING_LOCAL_RUNTIME.md).

The explanation library has its own single-request local provider, library service and a writing-only input factory.
The factory projects an immutable completed preview/unit/component into target text, exact UTF-16
occurrences and a local context receipt; it owns no live editor or durable notebook. Its full sentence
evidence tokenizer retains separator spaces separately from the existing trimmed caret tokenizer.
The service owns per-request facades and a bounded complete-record cache. AppHost now owns its
independent local settings/runtime controller, which loads on request and releases after 60 idle
seconds. The portable session now owns target state, saved restoration and revision-based
cancellation. The [writing sentence card](docs/LEARNING_EXPLANATION_CARD.md) now borrows that session
for explicit generation/stop/refresh, revalidating the original editor before request/display.
Native interaction and model execution remain unverified. See [the explanation map](docs/LEARNING_EXPLANATION_RULES.md)
and [app ownership](docs/LEARNING_EXPLANATION_RUNTIME.md).
The [contextual query library](docs/LEARNING_CONTEXTUAL_QUERY.md) now sequences unit/component
explanation and lazy dictionary pages with separate target/request identities. It borrows the
explanation service. The [word/expression card](docs/LEARNING_CONTEXTUAL_CARD.md) now connects its
tabs and exact examples with captured-editor guards; native interaction and text selection remain gaps.

Explicit replacement and a combined autocomplete/learning host walkthrough are follow-up slices
after captured ranges, IME and editor acknowledgement are verified. Broader model residency/power controls, dictionary, notebook, Anki, speech and history work follows the scoped preview port.

Do not reuse the suggestion coordinator's active session or a single global llama sequence for
learning or text-to-speech. A task runtime owns its model/profile and cancellation; switching a model
or stopping speech cannot silently invalidate an active autocomplete request. Inference may share a
native library, but mutable native contexts, cache state, prompt caches and file lifetimes must be
serialized within their owner. On Windows, Apple Intelligence is unavailable; model selection should
be based on Windows-supported local engines or an explicit endpoint.

The lexical index now ports explicit forms and known-pattern matching behind a platform text
adapter. It returns original-text occurrence ranges; Windows ICU and the .NET Unicode runtime
provide text primitives with the documented platform/version limits.
The shared lazy lexicon store and bounded local bundle source are registered once by AppHost.
Completed translations acquire separate annotations and double-click into the bundled dictionary
session; [card ownership and identity gates](docs/LEARNING_WORD_PREVIEW.md) record the supported subset.
[Source mappings and evidence](docs/LEARNING_LEXICAL_RULES.md) keep that library scope explicit.
The [offline resource/service mapping](docs/LEARNING_OFFLINE_LEXICON.md) records the worker ownership,
immutable dataset, Chinese language sharing, lookup and cancellation contracts.

The durable vocabulary model is separate from transient translation output. The macOS reference uses
stable entry UUIDs, stable sense UUIDs, example/usage UUIDs with source ranges and provenance, and
independent review-card identities. Preserve those semantics in any Windows schema and migration.
The [sense-content library](docs/VOCABULARY_CONTENT.md) now owns immutable text/image blocks and pure validation/HTML rules in Core. It supplies no archive, media or UI effects. The [usage/capture boundary](docs/VOCABULARY_CAPTURE.md) retains exact encountered ranges, provenance, authorship and stable usage IDs; its receipt rule is also used by transient explanations. The [notebook/review graph](docs/VOCABULARY_MODELS.md) now owns complete sense metadata, optional authored content/cards, plan history and remote binding values. Pure projections/merge return immutable snapshots; the [save/sense rules](docs/VOCABULARY_SENSE_RULES.md) also produce uncommitted drafts for captures, explicit merge/move and current-input explanation attachment. The [archive validator](docs/VOCABULARY_ARCHIVE_VALIDATION.md) provides pure graph/layout/receipt admission before a future durable commit. [Archive draft edits](docs/VOCABULARY_ARCHIVE_DRAFTS.md) adapt field-level concurrent merging, exact removals and stale explanation pruning. The [relational projection](docs/VOCABULARY_RELATIONAL_PROJECTION.md) now maps immutable entity headers and ownership rows. The [row JSON codec](docs/VOCABULARY_PAYLOAD_CODEC.md) preserves typed leaf field names and shapes. The [SQLite boundary](docs/VOCABULARY_SQLITE_TRANSACTIONS.md) now executes isolated native in-memory transactions. The [Windows file adapter](docs/VOCABULARY_WINDOWS_FILES.md) adds protected named-file access and restart tests. The [serialized archive](docs/VOCABULARY_ARCHIVE.md) joins these layers with lazy load, semantic admission and post-commit cache publication. AppHost now owns the [shared Store](docs/VOCABULARY_STORE.md) and wires saved explanation restoration/attachment. The [writing capture factory](docs/VOCABULARY_CARD_CAPTURE.md) now supplies full detached captures and the explanation-input projection. [Writing-card favorites](docs/VOCABULARY_FAVORITES.md) now bind completed sentence and dictionary targets to the shared Store for Save and exact Remove. The [notebook browser](docs/VOCABULARY_NOTEBOOK.md) now supplies one activating window, stable navigation and read-only list/detail browsing with worker filtering. The [entry editor](docs/VOCABULARY_EDITOR.md) now uses isolated full-entry drafts and the existing three-way archive merge for acknowledged Save; [Sense/usage actions](docs/VOCABULARY_ACTIONS.md) now connect stable-ID New/Merge/Delete/Move through that Store; the [notebook session library](docs/VOCABULARY_NOTEBOOK_SESSION.md) now owns per-usage explanations and explicit dictionary lifetime, now connected by [contextual explanation/adoption rows](docs/VOCABULARY_EXPLANATION_UI.md); the [dictionary disclosure](docs/VOCABULARY_DICTIONARY_UI.md) now connects explicit bundled lookup; media and other providers remain open. Generated explanations can be refreshed; user-authored notes and identity cannot be overwritten by a
refresh. The pure [export scope/material projection](docs/VOCABULARY_EXPORT_PROJECTION.md) now retains exact IDs and
reduces selected learning data without IO; [rendering](docs/VOCABULARY_EXPORT_NOTES.md), [archive saving](docs/VOCABULARY_EXPORT_SAVE.md) and
[notebook export UI](docs/VOCABULARY_EXPORT_WINDOW.md) now have separate adapters and evidence.
Notebook images, graph relationships, and export effects belong to a vocabulary store service,
not to a translation prompt or WPF window. No cloud sync is planned.

## Privacy and bounded context

Potential prompt context includes preceding and following field text, app identity, clipboard text,
OCR, and language-learning captures. Each source needs its own setting, eligibility policy, maximum
size, sanitizer, and test. Raw screenshots and UIA trees should never be prompt payloads. OCR should
be cleaned and capped before generation. Do not log user text, selections, OCR, prompts, generated
content, dictionary files, or endpoint credentials. Local use should not create a hidden network
request; endpoint transmission must match the selected task and destination.

The current Windows code includes optional app/clipboard context, secure-field exclusions, terminal
exclusions, and DPAPI-backed endpoint credentials. These are implementation evidence, not proof of
correct behavior in every host. In particular, freshness, IME composition, selection, clipboard
restoration, OCR, and native insertion need host-level Windows validation.

## Platform validation

Portable CI can build the Windows solution and run pure .NET tests and shared JSON behavior fixtures.
It must not download model weights or create/upload packages. Native UI Automation, WPF overlay
geometry, keyboard hook behavior, IME composition, clipboard formats, elevated-host behavior, and
GPU inference require Windows. Native x64 Windows is the primary verification target. An ARM Windows
VM on Apple silicon can help inspect ARM-specific behavior, but it cannot establish x64 runtime or
Vulkan GPU behavior.

The [review session library](docs/ANKI_REVIEW_SESSION.md) borrows the shared controller/Store and
keeps one window-local draft/baseline. Its partials preserve original metadata, guarded send and
media ordering; a future preparation window must own task cancellation and release its audio preview.

The [serial batch session](docs/ANKI_REVIEW_BATCH.md) owns frozen presentation rows and one cancellable
UI-context task. It delegates every send to the existing single-session transaction, resolving merged
aliases and skipping successful rows on retry. The future sheet must stop and join accepted work.

The [speech preference owner](docs/SPEECH_SETTINGS.md) borrows the shared preference document and
explicit credential/file boundaries. Core retains exact destination identity and Codable profile
values; paired asset edits restore only their model/decoder profile. Sampler edits apply to the next
request without invalidating playback. AppHost/provider/player/settings UI wiring remains separate.

The [speech observer and request history](docs/SPEECH_TELEMETRY.md) keep content-free stage timings
and runtime identity beside a bounded 100-row preference Store. Old runtime events may patch an
existing old metric before publication admission; generation/session/sequence guards protect the
current PID/assets. UI-context serialization and real event/error log sinks belong to composition.

The [PCM playback facade](docs/SPEECH_PCM_PLAYER.md) keeps the original UI-owned preparation barrier,
queue/drain events and revision-scoped renderer seam. It marshals native callbacks to the caller UI
context and tracks retirement through shutdown; an actual Windows audio renderer and controller
composition remain open. Portable and STA gate tests establish scheduling, not audible playback.


The [shared speech controller](docs/SPEECH_CONTROLLER.md) owns lazy catalog/model selection,
resolved clicked-text requests, token-scoped playback and exclusive caller-owned export. Required
platform adapters keep recognition, codecs, generation and model transfer outside orchestration.
Controller tasks drain explicitly, while export and PCM/native lifetimes remain host responsibilities.
It is not registered in AppHost; provider/device implementations and presentation remain open.


The [independent speech HTTP adapter](docs/SPEECH_ENDPOINT.md) owns one bounded transport per action,
with separate idle/resource deadlines and decoded audio limits. The independent audio generator borrows
it; optional speech destination/settings disclosure remain composition responsibilities. It shares
neither cookies, cancellation nor mutable endpoint state with completion or translation clients.


The [local speech runtime](docs/SPEECH_LOCAL_RUNTIME.md) owns one lazy helper session and shared
uncancelled retirement. Windows atomically creates the child in a kill-on-close job with explicit
stdio inheritance; low/high memory events request idle release. Session IO and receiver/observer
callbacks do not hold state locks, and host shutdown separately joins old readers. The audio
generator routes frozen local/endpoint choices without sharing completion/learning state. The native
model helper, audio devices, UI and AppHost activation remain separate work.


The [native speech helper](docs/SPEECH_NATIVE_HELPER.md) statically links its own fixed libmtmd
revision/streaming overlay in a separate process. The complete original generation loop delegates
only binary stdio, Unicode paths, parent observation and reference metadata to platform adapters.
CPU/native effect checks own no model weights or audio device. App copy, providers and activation
remain separate composition work.

## Current editor text boundary

The [current editor annotation adaptation](docs/VOCABULARY_EDITOR_CURRENT.md) explicitly advances
this module to Mac `cc468b89f5c360cc7210fac4f1a8b52197870bf2`. `VocabularyPolicy.ReconciledRanges`
retains expression/form precedence and raw UTF-16 cursor behavior. `FoundationSubstringMatcher`
isolated raw search from `FoundationComposedText` expansion/admission, with immutable embedded
Darwin observations and pinned Unicode properties. The shared map is reused by capture, archive,
notebook and explanation/review range consumers so newly re-anchored spans survive Save/display.
The modal owns cancellable background range work and rejects stale publication; Save waits for its
current result, while translations and other authored edits remain in the UI-owned draft.
Other modules retain their documented references; see [the stable delta](docs/MAC_STABLE_DELTA.md).
