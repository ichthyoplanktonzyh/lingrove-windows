# Expression candidate speech and vocabulary

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, read with `git show`:

- `Lingrove/UI/InlineFeatures/ExpressionAssistCandidateRow.swift`;
- `Lingrove/Services/Presentation/ExpressionAssistPanelController.swift`;
- `Lingrove/App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`;
- `Lingrove/Support/Vocabulary/VocabularyCaptureFactory.swift` and the vocabulary model;
- `LingroveTests/Support/Vocabulary/VocabularyCaptureFactoryTests.swift`, including
  `expressionAssistCandidateSavesBaseFormWithConstructedSentenceContext`;
- `LingroveTests/Services/Presentation/ExpressionAssistPanelTests.swift` and coordinator tests.

This is a complete extension of the [existing expression application](EXPRESSION_ASSIST_APPLICATION.md):
production entry, candidate UI, shared services, durable capture and retirement. It does not declare
the remaining native trigger/editor or physical acceptance gaps complete.

## Candidate flow and UI

The actual result request reaches the panel with its candidates. Each row preserves insertion text,
optional distinct dictionary base form, visible meaning hint and selected indicator, followed by
separate explicit speech/favorite targets. Clicking either action cannot invoke row insertion or
move keyboard focus away from the temporary description. Enter/Tab, arrow and Ctrl-number insertion,
IME/query revision and cancellation remain owned by the existing session.

WPF uses frameless action buttons, the existing favorite fish geometry, Segoe UI, shared light/dark
colors, selected outline, bounded candidate/outer scrolling and accessible action names. Saved,
pending, playing/preparing, unavailable and failure states remain explicit. Saved candidates directly
toggle exact Remove, as on Mac; ordinary writing cards retain their saved-item navigation menus.
Tool state updates retain selection and capture identity rather than recreating a candidate result.
Asynchronous tool state can change row height, so the real panel coalesces a post-layout placement
update without reactivating the host. This adapts IA/state, not Mac pixels or a second native panel.

## Shared owners and data

`AppHost` lends the existing canonical writing Store, capture factory and shared speech controller.
`ExpressionAssistCandidateTools` prepares detached captures once per request/result on a worker.
Old preparation cannot bind a new result, and navigation or Store notifications cannot regenerate
its encounter UUID. Native/editor handles and the request admission callback never enter a capture.
Store actions revalidate the live candidate/host before admission; accepted writes finish and join
even if the presentation closes. The session drains its panel's candidate actions before retirement.
No view creates or disposes another production Store, inference runtime or audio service.

The Core factory ports the original rule: a nonempty dictionary form is canonical; otherwise use
trimmed insertion text. At most one lexical word is a Word, otherwise an Expression. A hypothetical
example joins the original bounded left context, exact untrimmed candidate surface and right
context, retaining the trimmed surface's exact UTF-16 occurrence range and sentence scope. It is
marked Generated with AI meaning origin. The query is not substituted as a meaning, and no context
is invented for a contextless request. Unsupported target language or failed native lexical evidence
leaves saving unavailable rather than inventing English or a word count. A Windows process basename
is not relabeled as a Mac bundle identifier; production provenance stays absent at that boundary.

The transient example's source kind reaches the existing canonical projection and current-format
SQLite transaction. Its schema, Store, IDs, ACK and backup owner stay unchanged. The historical
entry-owned codec keeps its pinned captured-only JSON shape; it explicitly refuses generated intents
instead of silently dropping provenance. This is not legacy conversion or archive activation.

Speech uses an independent panel owner UUID plus `expression.candidate.<index>`, trims the candidate
surface and supplies the request's target-language hint to the same speech controller as other
cards. Destination disclosure is visible before the explicit click and follows that task's current
system/local/endpoint configuration; surrounding draft is not spoken or sent. Rendering/navigation
starts no audio. As in Mac's panel, already accepted playback may outlive hiding; the shared
controller retains its detached request. Closing detaches observers and joins Store/preparation work,
without disposing shared owners or stopping unrelated playback.

The Home/Welcome expression scene borrows its existing private in-memory Store and system-only sample
speech owner for these same controls. Leaving/hiding a scene retires its tool bindings. It cannot
write a production notebook, capture a real editor or borrow a production endpoint profile.

## Verification and remaining native work

The primary implemented this block. The Mac factory regression and additional surface/base-form,
UTF-16, generated/AI, contextless, identity and historical-codec admission checks passed with the
unchanged payload cases (17/17). Compiled expression/application/session/view, real canonical
favorites and Home/Welcome tests passed 59/59. These include Save/Remove without insertion, retaining
a second usage, stable encounter identity across navigation, explicit silent speech/Stop, stale
preflight, accepted-save drain, late preparation and panel/session task ownership.

The primary inspected actual light/dark, short-window and 1.5-scale component renders for independent
hit regions, readable hints/base forms, saved state, destination disclosure and scrolling. There is
no corresponding Mac screenshot reference. Full suites, independent review and final-head CI are
recorded separately when complete. Test effects use synthetic text, fake speech/transport and isolated
in-memory databases, with no user editor/notebook/clipboard, actual audio, model or live endpoint.

Non-ASCII native triggers, provider-specific focus/IME/insertion, actual speech/model/device output,
physical mouse hit testing, mixed DPI and screen readers remain final unified native acceptance.
This module does not supply arbitrary-screen OCR or source-language confidence recognition.
