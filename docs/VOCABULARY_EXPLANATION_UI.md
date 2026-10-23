# Notebook contextual explanation and explicit adoption

The notebook now connects the existing window-owned session library to per-example and standalone
explanation rows. It adapts the pinned Mac contextual presentation and button ordering. Browsing is
idle; only the explicit explanation button requests generation. No editor capture, new runtime,
provider, transmission path or replacement mutation algorithm is introduced.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac responsibility | Windows mapping |
| --- | --- |
| `UI/Vocabulary/VocabularyExplanationSection.swift`: saved occurrence, stored-meaning precedence, exact adoption destination | `App/UI/Vocabulary/VocabularyExplanationRow.cs` borrowing the shared Store and stable child session |
| `UI/InlineFeatures/LearningExplanationView.swift`: contextual meaning, collapsed Details, proposal, Replace, progress/error and button priority | Same row controls and existing `LearningExplanationCardPolicy`; no duplicate example block |
| `UI/Vocabulary/VocabularyNotebookView.swift`: detail identity by sense and Store updates preserving selection | `VocabularyNotebookWindow.Learning.cs` plus detail rendering, stable row keys and subscription retirement |
| `Services/Presentation/VocabularyWindowController.swift`: lazy ownership, ordinary hide/reopen, explicit selection | Existing Windows controller, now borrowing an AppHost session factory and configuration action |

AppHost supplies a lazy factory using its shared explanation service, dictionary provider and native
text adapter. The controller creates one session for one window; the window owns its disposal.
Each row observes one child and owns only controls, pending presentation and that subscription. The
Store and archive retain durable ownership. Their existing `AdoptExplanationAsync` verifies the
exact saved occurrence/input and resolves a moved usage by UUID before acknowledging an update.

The standalone row always shows the stored meaning. An example also shows stored wording when it
is user-authored or no result exists. Otherwise the generated meaning can lead. Details starts
collapsed, holds literal explanation prose, and shows a generated proposal and Replace only when
supplied stored wording differs canonically. Merely generating or viewing a proposal does not adopt
it. Adoption marks the acknowledged meaning user-authored. The existing AppHost explained-event
subscription may independently attach the full generated annotation; it does not replace meaning.

Stop cancels the displayed generation without preparing another row. Other actions first activate
the row (canceling siblings), then recheck row identity and the exact displayed state before
starting/refreshing or opening configuration. Worker notifications return to WPF and reject retired
or superseded state. Details collapses only when input changes. Same-input Store updates reuse row
controls and avoid replacing identical TextBox text, preserving expansion and text selection.

Ordinary close cancels work and retains rows/results/Details. Reopen refreshes them from current
session state; it does not generate. Explicit show-this-sense rebuilds row presentation. Navigation
or disposal unsubscribes old rows, so detached buttons lose authority. Admitted adoption survives
hide/navigation; the containing window tracks and drains its durable operation on retirement while
canceling only pending presentation. It never waits for an uncooperative borrowed model reply.

## Validation and remaining work

Eight added Windows WPF cases use real notebook windows, real child sessions and real memory SQLite
Store/archive. They exercise idle navigation; explicit start/stop/sibling cancellation; authored
meaning/proposal/adoption; same-input Details/text-selection preservation and changed-input reset;
generated-example versus standalone precedence; ordinary reopen versus explicit show and detached
buttons; configuration and a removed occurrence's late reply; immediate explicit navigation before asynchronous list refresh; and a blocked acknowledged adoption
across retirement with repeated-click rejection. The suite now has 103 cases in its WPF assembly.
These are executed on Windows CI. macOS performs a full Windows-targeting compile; a compile is not
an execution. The existing 24 original Mac session observations and 21 Swift verifiers remain.

Dictionary session ownership is now connected by [the explicit notebook disclosure](VOCABULARY_DICTIONARY_UI.md). Source routing, system/imported/online dictionaries, media preparation and
storage, speech, review/templates/Anki, graph, exports, backup/restore and the documented search
provider differences remain open. Routed buttons and WPF layout tests do not prove physical input,
external editor focus/IME, insertion acknowledgement, model execution or GPU behavior.
No installer, downloadable artifact or release is produced.
