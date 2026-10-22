# Lingrove for Windows Architecture

This is a maintainer map for the Windows port. The source and binaries still use Cotabby identifiers
during migration. The repository currently contains an inherited autocomplete implementation;
language-learning components described as future flows are architectural targets, not shipped
Windows behavior.

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

Short, representable text can be inserted as Unicode input. Long or complex text may require paste,
but the current `SendInputTextInserter` snapshots only clipboard text. Its source comment notes that
non-text clipboard formats can be lost. Treat rich-format preservation, concurrent-copy handling,
focused-target revalidation, and failure acknowledgement as open platform safety work before relying
on this path for language-learning replacement.

## Learning flow and independent runtimes

The Windows baseline has no language-learning coordinator, capture UI, dictionary or notebook. The
macOS source is the behavior reference for a future Windows subsystem. A suitable Windows flow is:

```text
committed typing / explicit selection / explicit region capture
  -> capture policy, current-field identity, IME and secure-field checks
  -> immutable, bounded LearningRequestContext
  -> learning task router + its own local runtime or explicitly selected endpoint
  -> translation / explanation / offline lexical word-expression recognition / dictionary lookup
  -> result card with provenance and safe replacement acknowledgement
  -> optional save to local vocabulary store -> notebook / Anki export or connection
```

The first Windows learning slice should stay narrow and exercise both core product loops end to
end: show a sentence-translation preview with a deterministic fake engine, then allow an explicit
replacement only after the captured field and selection still match. Pair that with accepting an
autocomplete suggestion in the same host field. A manually configured endpoint may be used during
host validation, but fixture and CI runs must use the fake and send no text over the network. Broader
dictionary, notebook, Anki, speech, and history work follows this vertical slice.

Do not reuse the suggestion coordinator's active session or a single global llama sequence for
learning or text-to-speech. A task runtime owns its model/profile and cancellation; switching a model
or stopping speech cannot silently invalidate an active autocomplete request. Inference may share a
native library, but mutable native contexts, cache state, prompt caches and file lifetimes must be
serialized within their owner. On Windows, Apple Intelligence is unavailable; model selection should
be based on Windows-supported local engines or an explicit endpoint.

The durable vocabulary model is separate from transient translation output. The macOS reference uses
stable entry UUIDs, stable sense UUIDs, example/usage UUIDs with source ranges and provenance, and
independent review-card identities. Preserve those semantics in any Windows schema and migration.
Generated explanations can be refreshed; user-authored notes and identity cannot be overwritten by a
refresh. Notebook images, graph relationships, and exports belong to a vocabulary store service,
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
