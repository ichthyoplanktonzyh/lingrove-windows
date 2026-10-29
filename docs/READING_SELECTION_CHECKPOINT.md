# Selected-text reading

Reference: macOS `3b53402f2cbc196b0398d133177fd80006d165b2`, including
`App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift`,
`Models/LanguageLearning/LearningReadingSelection.swift`,
`Services/LanguageLearning/LearningReadingSelectionReader.swift`,
`Support/LanguageLearning/LearningReadingPolicy.swift`, `LearningReadingLookupPolicy.swift`,
`LearningReadingLanguageResolver.swift`, `LearningReadingContextPolicy.swift`,
`LearningTranslationPrompt.swift`, `LearningLookupPolicy.swift`, `LearningLookupModels.swift`,
`LearningOutputGrammar.swift`, `LearningExplanationService.swift` and their tests. Git comparison
against `35b7fbe5c788c5524936ade1dfb7c89a3a285b04` found no changes in the reading coordinator,
language-learning models/services/support, learning settings or corresponding tests. Existing older
fixtures retain their own pins. Source-derived
C# cases are not direct Swift executions.

The manual shortcut (default Alt+Shift+T) is disabled by default and has an independent target
language. AppHost borrows the existing keyboard and focus owners, the native read-only selection
lease, the translation runtime and shared explanation instruction runtime. It verifies the original
process/field/epoch and receipt before presentation and after model work, and tracks cancellation
and shutdown. Endpoint translation requires explicit consent and displays its actual sanitized
HTTP(S) destination before provider work. It never borrows the autocomplete endpoint or runtime.
Resolved contexts require exact UTF-16 equality at the specified occurrence. Acquisition-window
alignment, including canonical/whitespace equivalence, replacement and range recalculation, is not
ported; a selected string is never found again by searching the document.

The selected word/phrase card requests a bounded basic pair from the local explanation model using
the shared instruction lease and fixed source grammar; details are a separate request bound to the
accepted pair. The bundled dictionary searches all resource languages when language is unknown.
The Windows translation endpoint handles translation only; lookup never silently borrows it. If the
explanation model is unconfigured, the card offers its settings and bundled dictionary. Explicit
copy uses the shared clipboard writer with final receipt admission. Favorites use the single
canonical Store after a fresh receipt check; an accepted lookup meaning or complete translation is
retained. Unknown-language generated details do not yet enter the older nonnullable explanation
record projection, so those details are not saved as explanation records.

This is the manual selected-text subset, not complete reading parity. No language recognition or
surrounding context is acquired in the current native adapter: source remains unknown and a lookup
is presented as standalone. ICU root-locale word segmentation is the Windows adaptation when the
language is unknown; Darwin tokenizer and ICU Unicode/version differences remain unverified. The
shortcut arbitration uses a cheap current foreground process/window/epoch snapshot; the native
reader independently validates readonly or editable selection evidence. Selection icon, pointer, screenshot/OCR and
clipboard sources are not implemented. No model, external endpoint, user editor or clipboard is
exercised by tests; real host UIA, physical IME, accessibility, DPI and model-backed behavior remain
acceptance gates.


The native reading lease exposes a nonblocking live permit based on retirement, its capture epoch
and current foreground HWND/PID. Copy checks it at the clipboard grant; favorite checks it at Store
acceptance; translation checks it immediately before HTTP send. Local translation/basic/detail
lookup carry it through the call gate and worker queue, after native memory clearing and immediately
before each prompt batch decode. These checks do not wait for asynchronous invalidation notification.
Already accepted Store work remains owned and drained. The permit contains no UIA call and is
request-scoped; ordinary translation without a permit retains its existing behavior. Held gate,
worker and native-clear tests plus invalidation between prompt batches exercise those final checks.
They do not establish physical desktop acceptance.
