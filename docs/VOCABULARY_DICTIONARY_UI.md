# Explicit notebook dictionary disclosure

The notebook now adapts the pinned Mac Dictionary section: expanding is an explicit query,
collapsing retires it, and same-sense Store updates retain the disclosure and viewport. It borrows
the window's existing dictionary session with no explanation service. Browsing and dictionary
navigation do not generate explanations or write dictionary content into the archive.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac responsibility | Windows mapping |
| --- | --- |
| `UI/Vocabulary/VocabularyDictionarySection.swift`: explicit disclosure, form change, disappear | `App/UI/Vocabulary/VocabularyDictionarySection.cs`, owned by the current notebook sense |
| `UI/InlineFeatures/LearningDictionaryContentView.swift`: definitions/attribution | Shared `App/UI/Learning/LearningDictionaryDefinitions.cs`, extracted from the existing floating card without changing its literal rendering or external-link admission |
| `Support/LanguageLearning/LearningDictionaryViewport.swift`: first resolved bounded height | Existing Core `LearningDictionaryViewport`, measured at the notebook's available width |
| `Services/Vocabulary/VocabularyNotebookSession.swift`: explicit show/hide/cancel/resume | Existing App session; native word preparation still runs away from the UI thread |

The section owns only controls, expansion, viewport measurement and subscriptions. The window
reuses it across updates to the same sense and retires it on navigation, explicit Show or shutdown.
A removed expanded section also cancels an unfinished native preparation, even before a dictionary
unit exists. Ordinary window hide preserves expansion; the window session cancels provider work
and Resume reopens only the previously explicit query. Hidden Store edits defer work; ordinary reopen synchronously refreshes
the current detail and resumes the expanded section with its latest acknowledged entry, avoiding
a cleared or stale remembered query. Word and Back actions carry the exact
rendered target state, so detached controls cannot navigate a later query. Worker notifications
return to the dispatcher and reject superseded state.

Both floating and notebook views use one literal definition renderer. It renders headwords,
phonetics, senses, examples, source and license links with the existing URL policy. It parses no
provider HTML and opens no link automatically. The existing 120–300 point viewport locks its first
resolved measurement per query. Dictionary source routing is still bundled-only: the UI says so,
and offers no nonfunctional system/imported/online source picker. A successful test provider hit
is not evidence that common-word fallback providers have been ported.

The source disclosure automatically requeries on form changes; language changes invalidate the
session without an automatic query. Windows adds one necessary asynchronous adaptation: if an
empty-form expression edit cancels unfinished native word preparation, the already-expanded
section starts preparation for the edited effective query. An already-open old query retains the
Mac form/language invalidation rule. An idle closed state is not presented as indefinite loading.
Preparation failure uses a fixed message; collapse/reexpand provides explicit retry.

## Validation and remaining work

Nine added Windows WPF tests use actual notebook controls, the shared renderer, production session,
memory SQLite Store/archive and real native Windows ICU. External dictionary responses and explicit
scheduling/failure gates are controlled. Cases cover lazy expansion and literal attributed bounded
content; expression word/Back cache and retired buttons; retained same-sense expansion and edited
form; collapse/late provider/ordinary reopen/explicit reset; retirement of blocked native preparation;
fixed failure/retry; deferred latest-entry resumption after hidden form/expression edits; and same-ID empty-form expression editing during blocked preparation. The WPF
assembly now has 112 cases. macOS compiles the full solution; Windows CI executes the tests. Existing
floating dictionary view tests protect the extracted renderer. No external links are opened by tests.

The original 24 Mac session observations and all 21 Swift verifiers remain unchanged. Native input,
external editor focus/IME, model/GPU execution, media preparation/storage, speech, review/templates,
Anki, graph, export and backup/restore remain open. Physical dictionary interaction and final window
geometry need an interactive Windows host. No installer, artifact upload or release is produced.
