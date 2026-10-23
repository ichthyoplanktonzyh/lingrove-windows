# Writing sentence explanation card

This slice connects the existing local explanation service/session to the manual writing
translation preview. Its reference is the committed Mac source at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, not the evolving Mac working tree.

| Mac source | Windows adaptation | Responsibility |
| --- | --- | --- |
| `Services/Presentation/LearningSentencePanel.swift`: `show`, `hide`, explanation ownership | `App/Learning/LearningPreviewController.Explanation.cs` and existing preview lifecycle | One lightweight session borrows the app-owned explanation service. Complete translation sets its target; hiding or retiring the editor intent clears it. |
| `Support/LanguageLearning/LearningExplanationPolicy.swift`: `input(presentation)`; `Support/Vocabulary/VocabularyCaptureFactory.swift`: writing `preview` | Existing `LearningExplanationInputFactory`, now composed with the lexical and raw-extent ICU adapters in `AppHost` | The target is the complete translated text in the translation's target language. The original sentence, reverse meaning and editor metadata do not enter the explanation input. |
| `UI/InlineFeatures/LearningExplanationView.swift`: `LearningExplanationButton`, embedded view | `LearningExplanationCardPolicy.cs`, `UI/Learning/LearningPreviewWindow.xaml(.cs)` | One header action, embedded complete meaning/prose, progress/error feedback, stop, refresh and configuration handoff. |
| `Services/LanguageLearning/LearningExplanationSession.swift` | Existing Core `LearningExplanationSession` | No generation on navigation; retain complete answers during refresh, stop or failure; reject retired replies. |

The controller owns captured-document authority; the Core session owns explanation state; the
existing app owner owns cache/model lifetime. Windows ICU word/sentence facts run on a worker,
after the translation is complete and validated. They do not run from a WPF render callback.
Pending preparation carries the original intent and a preparation revision. Before committing
its input, starting a request or showing a session snapshot, the controller checks the current
translation request, exact source/result/options, captured-field identity and eligibility.
UIA refreshes validate the frozen editor snapshot; they never replace the study material.
An unavailable tokenizer only removes the explanation target, leaving the successful translation.

Snapshots are marshalled to the Dispatcher and checked again after the worker/dispatcher gap.
The button supplies the exact immutable session snapshot it rendered. A queued stale Stop click
cannot become a new refresh after the generation completed in the meantime. A new hotkey intent,
typing, focus/selection change, changed translation settings, cancellation, close or app disposal
retires preparation and clears the session. Explanation profile/language changes use the existing
service/session cancellation path. Lexical recognition finishing on the same complete translation
does not reset the explanation or generate again.

Opening the preview never loads an explanation model. The header action generates explicitly;
while work is active the same action only stops it. An existing result or error requests refresh;
the previous complete answer remains visible. An unconfigured role opens the existing Learning
settings pane after retiring the captured preview. Settings selection does not generate. There is
no endpoint transport, text insertion or notebook adoption in this flow.

The current Windows preview already shows the source and translation together, so this slice
embeds explanation there; Mac compact/expanded card modes are not yet ported. Meaning and prose
are plain text with an Arabic response direction when appropriate. Both the explanation and the
whole card have bounded scroll viewports. The inherited `WS_EX_NOACTIVATE`, `ShowActivated=false`
and physical-pixel placement path remain the native focus/position boundary. Native interaction,
scrolling, DPI and multi-monitor work-area behavior need a Windows desktop walkthrough.

`LearningExplanationCardPolicyTests` compiles the actual non-WPF action/document rule. It checks
missing/retired/partial targets, exact request/text/options identity, canonical-equivalent but
different documents, lexical-stage continuity and action priority. Existing session, service,
runtime, capture fixture and Swift-oracle tests cover their separately documented boundaries.
These tests and a full WPF compile do not execute the dispatcher/UIA event sequence or paint a
native window. No model is downloaded or executed by CI; explanation quality remains unmeasured.

[Unit/component dictionary explanation](LEARNING_CONTEXTUAL_CARD.md) is now connected separately.
Source-word fallback, selection/reading/OCR captures,
notebook adoption/persistence, settings sample action, localization, telemetry and explicit
explanation endpoint consent/transport remain separate migration slices.
