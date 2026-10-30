# Feature Parity and Evidence

The current [settings-search adaptation](SETTINGS_SEARCH_3B.md) maps the 3b ranker and navigation
to Core, Windows ICU and WPF, including localized matching, keyboard opening, original-row reveal
and arrival lifetime. Its source/oracle, component checks and remaining native gates are recorded
separately; the historical baseline matrix below retains its references.

This matrix starts from the Windows fork baseline `b1e4031c70aca1be21aae2c57b138fde5fc22bb1` and
compares it with the committed macOS reference `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. It also
records the initial foundation branch's shared JSON corpus and its test evidence. The source paths
are reference points, not a claim that every Windows behavior has been validated. The Windows tree
still uses Cotabby names. The current learning addition is the scoped manual read-only preview
recorded in [LEARNING_PREVIEW.md](LEARNING_PREVIEW.md); the wider learning product remains incomplete.

The maintainer advanced the campaign to latest Mac functionality, currently resolved as
`3b53402f2cbc196b0398d133177fd80006d165b2`, recorded in
[MAC_LATEST_BASELINE.md](MAC_LATEST_BASELINE.md). Earlier module references, including
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, remain explicit until individually upgraded.
Functional, interaction and UI/design acceptance follow
[WINDOWS_ALIGNMENT.md](WINDOWS_ALIGNMENT.md). [MAC_STABLE_DELTA.md](MAC_STABLE_DELTA.md) retains
the earlier `cc468b89f5c360cc7210fac4f1a8b52197870bf2` audit as history, not the current backlog.
Existing corpus revisions remain fixed; [current editor annotations](VOCABULARY_EDITOR_CURRENT.md)
are an explicit module upgrade with 182 fresh source observations. Historical baseline statuses
below must not be read as a declaration of full current product parity.

The [latest autocomplete output adaptation](AUTOCOMPLETE_OUTPUT_CURRENT.md) is an explicit module
upgrade for newline boundaries, echo cleanup and preface truncation. Its four grouped regression
tests supplement the unchanged historical corpus; its final-head/native limits remain separate.

The [latest resource graph adaptation](VOCABULARY_RESOURCES_CURRENT.md) adds current canonical
values, joined/collected projection and lifecycle-reference admission. It does not activate a new
archive, migrate existing Windows data, implement full semantic validation or complete the latest
notebook UI; those remain explicit gaps beyond the older matrix rows.

The [canonical application composition](VOCABULARY_CANONICAL_APP.md) now selects the current-format
Store for the notebook and writing cards, using a fresh versioned file under the Cotabby data root.
Its browser/create/import/favorite behavior supersedes the old production notebook wiring.
Scoped [entry editing and file images](VOCABULARY_CANONICAL_ENTRY_EDITOR_UI.md),
[sense/usage commands](VOCABULARY_CANONICAL_SENSE_UI.md) and
[meaning-node/mapping editing](VOCABULARY_CANONICAL_MEANING_UI.md) now use the same owner.
The [3b meaning/classification upgrade](VOCABULARY_MEANING_CLASSIFICATION_3B.md),
[usage-pattern editor](VOCABULARY_CANONICAL_PATTERN_UI.md),
[independent Anki cards](ANKI_CANONICAL_CARD_WINDOW.md) and
[batch preparation](ANKI_CANONICAL_REVIEW_BATCH.md) now connect their scoped canonical workflows.
The [canonical graph](VOCABULARY_CANONICAL_GRAPH_3B.md) connects entry/sense and meaning/category
navigation to that same window and Store. [Canonical image operations](VOCABULARY_CANONICAL_ENTRY_EDITOR_UI.md)
and [full backup/restore](VOCABULARY_CANONICAL_BACKUP.md) now use its media and transaction owner.
The [current export flow](VOCABULARY_CANONICAL_EXPORT.md),
[notebook learning rows](CANONICAL_NOTEBOOK_LEARNING.md),
[shared speech composition](SPEECH_PRODUCTION_3B.md) and
[review audio](ANKI_CANONICAL_REVIEW_AUDIO.md) have their own scoped integration records.
The [current review preparation](ANKI_REVIEW_PREPARATION_3B.md) removes the extra WPF default/preserved
layout refusal and connects typed side previews without changing model/mapping/card identity;
it also records current review label coverage and supersedes stale media/template gap claims.
The [language coverage upgrade](INTERFACE_LANGUAGE_COVERAGE_3B.md) completes the Windows supplement's
15-locale coverage, adds Windows ICU quantity selection from unchanged Mac plural resources,
and connects further current settings/Home/model/canonical-notebook states while preserving raw
user/provider values. Further fixed-copy/domain-message coverage and Mac search ranking remain open.
The [reading/CLI/interface checkpoint](WINDOWS_READING_CLI_INTERFACE_CHECKPOINT_20261030.md)
now composes selection affordances, bounded context, pointer reading and restricted field-region OCR,
local automation through the same Store, launch-time interface language and shared opt-in request history.
The [explicit-region reading upgrade](READING_EXPLICIT_REGION_3B.md) removes the editable-field
requirement for the screenshot command and retains origin-window/privacy evidence through local OCR.
The [pointer OCR upgrade](READING_POINTER_OCR_3B.md) connects explicit visual-first acquisition,
one-frame spatial context and the strict UIA fallback. The
[selection OCR upgrade](READING_SELECTION_OCR_3B.md) connects optional exact-range visual enrichment,
gesture/geometry admission and preparation state. The
[read-only provider upgrade](READING_READONLY_PROVIDERS_3B.md) adds positive TextPattern-only
selection/ancestor support; actual host coverage, remaining media variants and physical acceptance
stay separately scoped.
This is an implementation
checkpoint with scoped automated/render evidence, not full native desktop parity.

The [writing sentence flow](WRITING_TRANSLATION_3B.md) now connects automatic previews, explicit
draft context and previous-sentence evidence. The [writing range transaction](WRITING_RANGE_REPLACEMENT_3B.md)
adds exact selected text and Unicode sentence scopes, with native selection/value acknowledgement
and Copy recovery; unsupported providers and physical host acceptance remain explicit.
The [online/website dictionary flow](DICTIONARY_WEBSITE_ONLINE_3B.md)
adds explicit online offers and browser destinations to the shared dictionary sessions, with
bounded transport ownership and native WPF source settings. Their local/CI and physical acceptance
records remain distinct; the historical matrix below retains its own references.

The [Home/Welcome examples](HOME_WELCOME_SHOWCASE_3B.md) now connect their complete seven-scene
walkthrough and explicit example windows using real components and isolated in-memory owners.
Illustrated reading acquisition and sample replacement are not native host feature acceptance.

The [current expression candidate tools](EXPRESSION_CANDIDATE_TOOLS_3B.md) connect explicit row speech
and acknowledged Save/exact Remove to the same production speech/Store owners, preserve generated
example and AI meaning provenance, and use those controls in the isolated Home/Welcome example.
Native trigger/editor, audio/device and physical UI acceptance remain separately scoped.
The [literal Unicode trigger adaptation](EXPRESSION_UNICODE_TRIGGER_3B.md) connects fullwidth and
other policy-admitted symbol markers to acknowledged native range commits for acceptance and cancel;
physical host/IME/input acceptance remains open.

## Status meanings

The [expression assistance application composition](EXPRESSION_ASSIST_APPLICATION.md) connects
frozen request/output rules, shared explanation instruction leases, temporary WPF input and a
limited native editor adapter. Its compiled checks do not establish physical editor, IME,
clipboard or model-backed acceptance; unsupported providers/triggers remain explicit gaps.

- **verified** — the behavior has passing automated parity evidence and, where it touches Windows
  UIA, input, display, storage, or inference hardware, has passed the relevant native host check.
- **implemented-unverified** — Windows source implements the behavior, but evidence is limited to
  source inspection or portable unit tests; native user-host behavior has not been confirmed.
- **platform-adapted** — Windows requires a distinct API or runtime path; implementation exists or is
  planned, and its Windows-specific behavior still needs host validation.
- **source-only** — the macOS reference contains the feature and tests, but no corresponding Windows
  product implementation was found at the Windows baseline.
- **missing** — a required contract or safety behavior is not present in the Windows baseline, even
  though the rest of the feature may exist. This marks a parity gap rather than a source-file search
  result.

Only behavior with scoped passing evidence is marked verified. Unit tests do not establish native
Windows behavior for UIA, input, rendering, insertion, persistence, or inference hardware.

## Autocomplete

| Capability | macOS reference at pinned commit (source / tests) | Windows baseline evidence | Status |
| --- | --- | --- | --- |
| Request, prompt, and continuation policies | `Cotabby/Support/Suggestion/Request/SuggestionRequestFactory.swift`, `Cotabby/Support/Prompting/PromptSectionBudget.swift`, `CotabbyTests/Support/Suggestion/Request/SuggestionRequestFactoryTests.swift` | `src/Cotabby.Core/Suggestions/SuggestionRequestFactory.cs`, `src/Cotabby.Core/Support/BaseCompletionPromptRenderer.cs`, `tests/Cotabby.Core.Tests/Suggestions/SuggestionRequestFactoryTests.cs`, `tests/Cotabby.Core.Tests/Support/Prompting/PromptSectionBudgetTests.cs` | implemented-unverified |
| Output cleanup and session reconciliation | `Cotabby/Support/Suggestion/Output/SuggestionTextNormalizer.swift`, `Cotabby/Support/Suggestion/Session/SuggestionSessionReconciler.swift`, matching `CotabbyTests/Support/Suggestion` tests | `src/Cotabby.Core/Support/SuggestionTextNormalizer.cs`, `src/Cotabby.Core/Suggestions/SuggestionSessionReconciler.cs`, matching `tests/Cotabby.Core.Tests/Support` and `Suggestions` tests | implemented-unverified |
| Typing, word/phrase acceptance, CJK chunks, double-tap and tail continuation | `Cotabby/App/Coordinators/Suggestion/`, `Cotabby/Support/Suggestion/Acceptance/`, `CotabbyTests/App/Coordinators/Suggestion/` | `src/Cotabby.Core/Suggestions/AcceptanceChunker.cs`, `SuggestionCoordinator.cs`, `SuggestionSessionReconciler.cs`; `tests/Cotabby.Core.Tests/Suggestions/` | implemented-unverified |
| UIA focus, caret, selection, secure-field and app eligibility | `Cotabby/Services/Focus/`, `Cotabby/Models/Focus/`, `CotabbyTests/Services/Focus/` and `CotabbyTests/Support/Accessibility/` | `src/Cotabby.Win32/Focus/UiaFocusTracker.cs`; `src/Cotabby.Core/Focus/`; `tests/Cotabby.Core.Tests/Focus/` | platform-adapted |
| Stale capture invalidation before paint and insertion | `Cotabby/App/Coordinators/Suggestion/SuggestionCoordinator+Prediction.swift`, `SuggestionCoordinator+Acceptance.swift`, `CotabbyTests/App/Coordinators/Suggestion/SuggestionFocusFreshnessTests.swift` | `src/Cotabby.Core/Suggestions/SuggestionCoordinator.cs`, `SuggestionWorkController.cs`, `src/Cotabby.Win32/Focus/UiaFocusTracker.cs` | implemented-unverified |
| Ghost text geometry, multiline wrapping, DPI and host-specific placement | `Cotabby/Services/Presentation/OverlayController.swift`, `Cotabby/Support/Presentation/`, layout and caret tests under `CotabbyTests/` | `src/Cotabby.App/Overlay/GhostOverlayWindow.xaml(.cs)`, `src/Cotabby.Core/Overlay/OverlayStyle.cs`; no native host evidence in this baseline | platform-adapted |
| Global hotkey observation and safe acceptance | `Cotabby/Services/Input/`, `Cotabby/Models/Input/`, suggestion coordinator tests | `src/Cotabby.Win32/Input/KeyboardHook.cs`, `src/Cotabby.Core/Input/`, acceptance tests | platform-adapted |
| IME preedit/composition exclusion | `Cotabby/Support/LanguageLearning/LearningInputMethodPolicy.swift` and `CotabbyTests/Support/LanguageLearning/LearningInputMethodPolicyTests.swift` document learning input behavior; autocomplete input tests also cover committed events | No dedicated Windows composition signal or IME policy found | missing |
| Local model inference and model lifecycle | `Cotabby/Services/Runtime/Llama/`, `CotabbyInference`, runtime tests/evals | `src/Cotabby.Inference/LlamaRuntimeCore.cs`, `LlamaRuntimeManager.cs`, `LlamaSuggestionEngine.cs`, `ModelDownloader.cs`; inference smoke tool | platform-adapted |
| Optional OpenAI-compatible autocomplete endpoint | `Cotabby/Services/Runtime/OpenAICompatible/`, endpoint policy and runtime tests | `src/Cotabby.Inference/OpenAICompatible/`, `src/Cotabby.Win32/Security/DpapiOpenAICompatibleCredentialStore.cs`, endpoint tests | implemented-unverified |
| Optional app and clipboard context | `Cotabby/Services/Context/`, `Cotabby/Support/Context/`, sanitizer and request tests | `src/Cotabby.Core/Context/`, `src/Cotabby.Win32/Clipboard/ClipboardTextReader.cs`, context tests | implemented-unverified |
| OCR or visible-screen context | `Cotabby/Services/Visual/`, `Cotabby/Support/Context/OCRTextHygiene.swift`, `CotabbyTests/Services/Visual/` | No screenshot capture/OCR subsystem found | source-only |
| Predict-ahead and streaming partial suggestions | `Cotabby/Support/Suggestion/Streaming/`, `Cotabby/App/Coordinators/Suggestion/SuggestionCoordinator+TypingPrediction.swift`, related coordinator tests | No matching streaming/predict-ahead state found | source-only |
| Emoji, inline macros, and autocorrect | `Cotabby/Services/Spelling/`, `Cotabby/Support/Spelling/`, inline feature coordinators and tests | `src/Cotabby.Core/Emoji/`, `Macros/`, `Spelling/`; `src/Cotabby.Win32/Spelling/WindowsSpellChecker.cs`; corresponding Core tests | implemented-unverified |
| Per-app behavior, pause, power profiles, settings and performance controls | `Cotabby/Models/Settings/`, `Cotabby/Support/Settings/`, `Cotabby/Services/Power/`, settings tests | `src/Cotabby.App/Settings/`, `src/Cotabby.App/UI/Settings/`, `src/Cotabby.Win32/Power/`, Core settings/pause tests | implemented-unverified |

## Language learning

| Capability | macOS reference at pinned commit (source / tests) | Windows baseline evidence | Status |
| --- | --- | --- | --- |
| Translation while writing, task settings and request profiles | `Cotabby/Services/LanguageLearning/LearningTranslationService.swift`, `LearningExpressionService.swift`, `Cotabby/Models/LanguageLearning/`, `CotabbyTests/Services/LanguageLearning/LearningTranslationStyleIntegrationTests.swift` | `Core/LanguageLearning/`, `Inference/LanguageLearning/`, `App/Learning/`, `App/UI/Learning/`: manual read-only current-sentence preview, independent endpoint/config/cancellation, bounded safe streaming prefixes and completed-result cache; explicit [source/complete-translation Copy](LEARNING_COPY.md) with captured-document guards; local CPU runtime, exact-token planning, GGUF template resolution and explicit model import/catalog selection; multi-source preference/ranking policy ported but production recognition unavailable; [source/test mapping and limits](LEARNING_PREVIEW.md) | implemented-unverified |
| Safe translation replacement with acknowledgement | `Cotabby/Services/LanguageLearning/LearningSelectionReplacement.swift`, `Cotabby/Support/LanguageLearning/LearningReplacementVerificationPolicy.swift`, matching tests | No learning replacement flow or host acknowledgement. Shared `GuardedTextInserter` and Win32 clipboard lease now provide guarded dispatch and bounded supported-format preservation; [limits and evidence](EDITOR_SAFETY.md) | missing |
| Selected text and reading capture | `Cotabby/App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift`, `Cotabby/Services/LanguageLearning/LearningReadingSelectionReader.swift`, `LearningReadingSourceReader.swift`, `CotabbyTests/App/Coordinators/InlineFeatures/LearningReadingCoordinatorTests.swift` | No selection/reading learning capture | source-only |
| Explicit screen-region capture and OCR | `Cotabby/Services/Presentation/LearningRegionSelectionPanel.swift`, `Cotabby/Services/Visual/ScreenTextExtractor.swift`, `CotabbyTests/Services/Visual/ReadingOCRIntegrationTests.swift` | No screen capture/OCR subsystem | source-only |
| Sentence explanation and offline lexical word/expression recognition | `Cotabby/Services/LanguageLearning/LearningExplanationService.swift`, `LearningExpressionService+Sentence.swift`, `Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift`, `LearningUnitRecognizer.swift`, `VocabularyCaptureFactory.swift`, matching policy/recognizer/capture tests | Completed translations receive independent lexical units and bundled double-click lookup; 34 lexical +19 link/viewport Mac cases. Explanation rules have 68 Mac cases, capability/output gates another 52, writing capture-to-input another 33; a local library provider joins metadata/exact-token/EOG/JSON checks. A library service adds frozen profile/language identity, saved-first lookup and a bounded complete-record cache. The [shared vocabulary Store](VOCABULARY_STORE.md) now supplies actual saved lookup/restoration and generated-record attachment. AppHost now owns independent local settings/runtime and the on-demand/60-second idle policy; a portable session handles saved restoration/refresh/cancellation. The writing sentence card now connects explicit explanation/stop/refresh with captured-document checks; native interaction/model execution are unverified. The unit/component WPF card now connects contextual meaning, exact examples and lazy dictionary navigation; [mouse/context selection Copy](LEARNING_TEXT_SELECTION.md) now shares captured-document/target grants; keyboard/word-double-click/autoscroll selection, telemetry, endpoint and source-word fallback remain open; [lexical scope](LEARNING_LEXICAL_RULES.md), [explanation scope](LEARNING_EXPLANATION_RULES.md) | implemented-unverified |
| Offline lexicon and system dictionary lookup | `Cotabby/Services/LanguageLearning/LearningLexiconService.swift`, `SystemDictionaryArticleService.swift`, `CotabbyTests/Services/LanguageLearning/LearningLexiconServiceTests.swift` | Pinned resource, bounded decoder/source, shared lazy store and bundled target-translation card adapted. The [contextual session library](LEARNING_CONTEXTUAL_QUERY.md) adds contextual default, lazy dictionary pages and target lifetime, now connected by the [WPF adapter](LEARNING_CONTEXTUAL_CARD.md). System/imported/online providers remain unported; [mapping and evidence](LEARNING_OFFLINE_LEXICON.md) | implemented-unverified |
| Imported MDX/MDD dictionaries and dictionary website providers | `Cotabby/Services/LanguageLearning/MDictLibraryService.swift`, `LearningDictionaryRouter.swift`, `OnlineDictionaryClient.swift`, `CotabbyTests/Services/LanguageLearning/DictionaryProviderTests.swift` | No dictionary subsystem | source-only |
| Stable vocabulary, senses, usages, notes and exact spans | `Cotabby/Models/Vocabulary/VocabularyModels.swift`, `VocabularyContentModels.swift`, `Cotabby/Services/Vocabulary/VocabularyStore.swift`, `CotabbyTests/Services/Vocabulary/VocabularySenseTests.swift` | Pure [sense-content values/rules](VOCABULARY_CONTENT.md) ported with 72 actual Mac fixture cases; usage/capture values and exact identity/admission have 63 actual Mac probes; entry/sense/review in-memory graph has 31 actual Mac model/projection/merge probes; pure [save/sense ownership rules](VOCABULARY_SENSE_RULES.md) add 33 actual Mac mutation probes; [whole-graph admission](VOCABULARY_ARCHIVE_VALIDATION.md) adds 124 actual Mac archive/layout/receipt cases; [concurrent edit/pruning drafts](VOCABULARY_ARCHIVE_DRAFTS.md) add 24 actual Mac helper cases; [typed relational projection](VOCABULARY_RELATIONAL_PROJECTION.md) adds 26 actual Mac mapping/reconstruction cases; [row JSON encoding/decoding](VOCABULARY_PAYLOAD_CODEC.md) adds 40 actual Mac leaf/decoder probes; [SQLite in-memory transactions](VOCABULARY_SQLITE_TRANSACTIONS.md) add 20 actual Mac native-SQL probes; the [Windows file adapter](VOCABULARY_WINDOWS_FILES.md) adds 37 native file/SQLite cases; the [serialized archive](VOCABULARY_ARCHIVE.md) adds 22 complete original Mac actor observations, six portable lifetime tests and two Windows archive/file cases; the [AppHost-owned Store](VOCABULARY_STORE.md) adds 16 original Mac store observations and eight portable ownership tests, with saved explanation callbacks; the [writing capture factory](VOCABULARY_CARD_CAPTURE.md) adds 33 full Mac capture observations and three identity/provenance tests; [writing-card favorites](VOCABULARY_FAVORITES.md) connect Save/exact Remove with six WPF archive-backed cases; the [notebook browser](VOCABULARY_NOTEBOOK.md) adds stable navigation/read-only list/detail, 51 actual Mac view/policy probes, 26 actual platform text comparisons and six WPF cases; the [entry editor](VOCABULARY_EDITOR.md) adds isolated drafts, 29 original Mac closure cases and seven WPF cases; [sense/usage actions](VOCABULARY_ACTIONS.md) add acknowledged New/Merge/Delete/Move with eight WPF cases; [notebook session ownership](VOCABULARY_NOTEBOOK_SESSION.md) adds 24 original Mac observations and 33 dispatcher cases, now connected by [contextual explanation/adoption rows](VOCABULARY_EXPLANATION_UI.md) with eight WPF cases; [dictionary disclosure](VOCABULARY_DICTIONARY_UI.md) adds explicit bundled lookup and nine WPF cases; three collation differences and media/review/export remain open | implemented-unverified |
| Rich notebook, images, graph and export | `Cotabby/Services/Vocabulary/VocabularyNotebookSession.swift`, `VocabularyImageStorage.swift`, `VocabularyGraphLayoutWorker.swift`, `VocabularyArchive+Export.swift`, `CotabbyTests/UI/Vocabulary/` | Read-only [notebook list/detail browser](VOCABULARY_NOTEBOOK.md) with stable sense navigation; pure text/image-reference validation and literal review HTML library; explicit [entry/text editing](VOCABULARY_EDITOR.md) now uses acknowledged Store updates; [sense/usage actions](VOCABULARY_ACTIONS.md) now connect New/Merge/Delete/Move; [contextual explanation/adoption](VOCABULARY_EXPLANATION_UI.md) now borrows the shared sessions; [dictionary disclosure](VOCABULARY_DICTIONARY_UI.md) now connects explicit bundled lookup; [image preparation](VOCABULARY_IMAGE_PREPARATION.md) supplies bounded frame-zero PNG drafts with ten original Mac observations and 26 native cases; [image storage](VOCABULARY_IMAGE_STORAGE.md) adds actual private media files and reuse/repair with twelve original Mac observations and seven extra native cases; [archive media transactions](VOCABULARY_MEDIA_TRANSACTIONS.md) now supply staging/SQL/cleanup with twelve original Mac observations and nine native file cases; [image UI](VOCABULARY_IMAGE_UI.md) now connects file import/draft previews/reading/enlargement with sixteen Windows native cases; [existing-file drops](VOCABULARY_FILE_DROP.md) add the Unicode Shell compatibility path with 21 Windows message/control cases; [clipboard transfer](VOCABULARY_CLIPBOARD_TRANSFER.md) adds bounded encoded-image extraction with 48 reader/protocol/process cases and one helper startup case; [image paste](VOCABULARY_IMAGE_PASTE.md) now connects the shared owner to the button and explanation-section command with twelve WPF cases; [DIB adaptation](VOCABULARY_CLIPBOARD_DIB.md) adds bounded bitmap envelopes with 53 structure/color-evidence, thirteen WIC/pixel and one direct native frame cases; [export scope/material projection](VOCABULARY_EXPORT_PROJECTION.md) adds 37 actual Mac observations and five immutable/privacy regressions; [CSV-cell/UTF-8 preview boundaries](VOCABULARY_EXPORT_TEXT.md) add 46 actual Mac formatter observations; [complete TXT/CSV rendering](VOCABULARY_EXPORT_NOTES.md) adds 42 actual Mac byte/budget observations and two label/culture regressions; [portable JSON encoding](VOCABULARY_EXPORT_JSON.md) adds 19 actual Mac encoder/whole-export-budget observations; [prepared packages](VOCABULARY_EXPORT_PACKAGE.md) add 33 actual Mac factory/value observations and four boundary regressions; [archive export snapshots](VOCABULARY_EXPORT_SNAPSHOT.md) add 21 complete original Mac archive observations, six portable sequencing cases and one extra Windows shared-lock case; [stored ZIP encoding](VOCABULARY_EXPORT_ZIP.md) adds 36 actual Mac byte/path/count/budget observations and two independent decoder/ownership regressions; [atomic export saving](VOCABULARY_EXPORT_SAVE.md) adds 19 complete original Mac file observations and 34 extra Windows file/archive regressions; [Store/export session](VOCABULARY_EXPORT_SESSION.md) adds 16 actual Mac state observations and seven extra controlled lifetime/retry cases; [notebook export commands/form](VOCABULARY_EXPORT_WINDOW.md) add 15 actual Mac offer observations, twelve WPF control/lifetime cases and eleven native Shell startup/admission cases without showing its modal UI; [full-notebook backup codec](VOCABULARY_BACKUP_CODEC.md) adds 53 actual Mac Codable observations and eight ownership/strict-input cases, the [archive full backup](VOCABULARY_BACKUP_ARCHIVE.md) adds 22 complete original Mac file/snapshot observations and three extra native lifetime/dedup cases; [archive restore](VOCABULARY_RESTORE.md) adds 27 complete original Mac transaction observations, 22 portable admission-prefix cases and five extra native cancellation/conflict/lifetime cases; [Store backup/restore](VOCABULARY_BACKUP_STORE.md) adds 13 actual Mac state/media observations and four native publication/lifetime cases; [backup/restore native panels](VOCABULARY_BACKUP_DIALOGS.md) add twelve native startup/configuration/thread cases without Show; [notebook backup/restore UI](VOCABULARY_BACKUP_UI.md) adds fourteen compiled control/owned-confirmation/lifetime cases using actual Store/files/media/SQL; [graph values/projection/camera](VOCABULARY_GRAPH_VALUES.md) add 45 actual Mac observations, three explicit text-provider comparisons and one ownership case; the [original Grape particle engine](VOCABULARY_GRAPH_LAYOUT.md) adds 31 actual package snapshot cases and two ownership/buffer tests; the [serialized graph worker](VOCABULARY_GRAPH_WORKER.md) adds 27 actual actor transcripts and two ordering/context tests; the [graph preference/presentation library](VOCABULARY_GRAPH_PRESENTATION.md) adds 34 actual Codable/window-state cases and five extra boundary cases; the [compiled graph canvas](VOCABULARY_GRAPH_CANVAS.md) adds thirteen WPF component cases with real worker/rendering/peers; the [notebook graph integration](VOCABULARY_GRAPH_NOTEBOOK.md) connects original mode/chrome/settings with twenty-one compiled Store-backed WPF cases and three portable reset-scope cases; the [current stable sidebar](VOCABULARY_NOTEBOOK_CURRENT.md) adds persisted density, direct singleton selection, stable multi-sense header interaction and selected-child reveal, with Windows execution/physical acceptance still required; compressed/profiled bitmap variants, virtual/pixel drops and physical native-picker interaction remain open | source-only |
| Anki review, card/media handling and connection | `Cotabby/Services/Review/AnkiConnectClient.swift`, `AnkiReviewSession.swift`, `Cotabby/Models/Review/AnkiWireModels.swift`, `CotabbyTests/Services/Review/AnkiReviewTests.swift` | Durable review/layout/audio-receipt values and pure binding merge ported; [original card material](REVIEW_CARD_MATERIAL.md) adds175 actual-source content/layout/template/JSON observations plus3 immutable/enum/path-boundary cases; [local Anki transport](ANKI_CONNECT_TRANSPORT.md) adds88 original-client observations and15 boundary tests; [field/conflict preparation](ANKI_FIELD_POLICY.md) adds76 actual-source observations and2 owned-value/admission cases; [spoken-text and audio freshness](SPEECH_TEXT_POLICY.md) adds47 actual-source observations and2 .NET boundary cases; [in-memory audio export](SPEECH_EXPORT_POLICY.md) adds73 actual-source byte/filename observations and2 ownership/header cases; [review preferences](ANKI_REVIEW_SETTINGS.md) add60 complete original settings traces and3 explicit-read/reset/write cases; a separate [Windows DPAPI credential adapter](ANKI_CREDENTIAL_STORAGE.md) adds14 native cases; the [review controller/navigation](ANKI_REVIEW_CONTROLLER.md) adds167 original full-controller traces plus3 suspended/cancellation cases and1 owned-value case; [review/template persistence](VOCABULARY_REVIEW_TRANSACTIONS.md) adds56 original Store/archive traces,1 close/drain case and3 native media/SQL cases; [review settings UI](ANKI_REVIEW_SETTINGS_UI.md) adds one inert AppHost owner, original key-method16 traces,7 lifecycle/interaction cases and6 compiled WPF cases; [review sessions](ANKI_REVIEW_SESSION.md) add57 full-source flow traces and2 Windows named-reopen/dispatcher cases for draft/CAS, send, conflicts and audio/images; [serial batch sessions](ANKI_REVIEW_BATCH.md) add34 full-source flow traces and1 Windows task-publication/cancel case; review navigation UI, preparation/batch windows and conflict-choice UI remain open | partial |
| System, local and endpoint text-to-speech | `Cotabby/Services/Speech/`, `Cotabby/Models/Speech/`, `CotabbyTests/Services/Speech/` | [Spoken-text](SPEECH_TEXT_POLICY.md) and [encoded export](SPEECH_EXPORT_POLICY.md) policies exist; [PCM foundation](SPEECH_PCM_FOUNDATION.md) adds64 complete-original observations and2 ownership cases for Float32 blocks and queue accounting. [Pipe framing](SPEECH_FRAME_DECODER.md) adds122 complete-original traces and2 owned/shared-boundary cases; [request export accumulation](SPEECH_EXPORT_BUFFER.md) adds35 original actor traces,1 concurrent worker case and2 detached-value/admission cases. [Provider metadata](SPEECH_PROVIDER_METADATA.md) adds196 source observations plus immutable companion ownership. [Phrase/context language resolution](SPEECH_LANGUAGE_RESOLUTION.md) adds191 original-source observations, two measured Unicode sets and3 consistency/fault cases. [Voice ranking/selection](SPEECH_VOICE_SELECTION.md) adds112 original cases/113 captured host observations with injected source collation plus2 value/ownership cases. [Recording preparation](REVIEW_AUDIO_PREPARATION.md) adds45 original class traces,1 full-error trace and3 ownership cases. [Speech preferences](SPEECH_SETTINGS.md) add144 complete-original settings/identity observations and3 value/reset/publication cases. [Speech observation/shared history](SPEECH_TELEMETRY.md) add78 complete-original telemetry/Store observations and3 ownership/reset/Windows-persistence cases. [PCM playback facade](SPEECH_PCM_PLAYER.md) adds33 complete-original traces,6 portable lifecycle cases and1 compiled STA case. [Shared speech control/export](SPEECH_CONTROLLER.md) adds104 original observations,4 portable adapter facts and1 compiled STA case. [Independent speech HTTP](SPEECH_ENDPOINT.md) adds38 source observations (including5 real loopback compression cases) and5 adapter cases. [Local runtime/process ownership](SPEECH_LOCAL_RUNTIME.md) adds17 complete-original real-process observations, portable lifetime/routing tests and Windows native process/pressure component tests. [Native speech source/build](SPEECH_NATIVE_HELPER.md) adds the complete original generation loop, exact separate dependency overlay and model-free binary/platform gates; CPU build is not model-backed generation evidence. [Windows tempo/EOF ownership](SPEECH_PCM_TEMPO.md) adds actual managed pitch-preserving streaming DSP and a separately tracked output-tail barrier. [Windows PCM output](SPEECH_PCM_DEVICE.md) adds the compiled MTA WASAPI device, gap-aware raw-clock accounting, continuous tempo renderer and fine-grained input admission. [Encoded speech decoding](SPEECH_AUDIO_CODEC.md) adds bounded native in-memory WAV/MP3 and joined export-duration validation. Physical audio acceptance, installed inventory, Windows recognition/collation, encoded playback/system providers/model-backed local generation, AppHost/settings UI and review-audio integration remain work | source-only |

Vocabulary data needs a deliberate schema. In the reference, a vocabulary entry has a stable UUID;
each sense has its own stable UUID and owns meanings, notes, examples, and review state; each usage
example has its own UUID and exact source ranges. Rewording a sense does not change its identity, and
review-card identity stays independent. Windows migrations must preserve these relationships. This
does not imply that Windows storage is already compatible with the macOS database.

## Supporting product capabilities

| Capability | macOS reference at pinned commit (source / tests) | Windows baseline evidence | Status |
| --- | --- | --- | --- |
| Typing history, privacy controls and phrase prediction | `Cotabby/Services/History/`, `Cotabby/Services/Runtime/TypingHistoryPhraseEngine.swift`, matching `CotabbyTests/Services/History/` | Suggestion metrics exist, but no typing-history store/index/phrase engine found | source-only |
| Suggestion/runtime/system metrics | `Cotabby/Models/Runtime/Metrics/`, `Cotabby/Models/Suggestion/SuggestionQualityMetricsStore.swift`, runtime tests | `src/Cotabby.Core/Suggestions/SuggestionMetrics.cs`, `tests/Cotabby.Core.Tests/Suggestions/SuggestionMetricsTests.cs`; the [speech observer/shared history](SPEECH_TELEMETRY.md) adds original stage/runtime admission and bounded opt-in Store values, with AppHost/controller/performance UI wiring still open | implemented-unverified |
| Shared JSON behavior corpus (17 pure-policy cases) | `Cotabby/Support/Suggestion/Acceptance/`, `Cotabby/Support/Suggestion/Output/GhostSpaceBoundary.swift`, `SuggestionTextNormalizer.swift`; pinned tests recorded per case in `fixtures/parity/README.md` | `fixtures/parity/v1.json`, `fixtures/parity/README.md`, `tests/Cotabby.Core.Tests/Parity/VersionedParityCorpusTests.cs`, and `scripts/verify-macos-parity.py` check acceptance chunks, ghost spacing, and normalization in both runtimes | verified |
| Vocabulary CLI and mailbox protocol | `Cotabby/Services/CLI/`, `Cotabby/Models/CLI/`, `CotabbyTests/Services/CLI/VocabularyCLITests.swift` | No vocabulary CLI or mailbox | source-only |
| Interface localization and learning/settings search | `Cotabby/Models/Settings/InterfaceLanguage.swift`, `Cotabby/Support/Settings/InterfaceLanguageNormalizer.swift`, settings views/tests | WPF settings exist, but no interface-language system found | source-only |
| Durable task-specific settings | `Cotabby/Models/Settings/`, `Cotabby/Support/Settings/SuggestionSettingsStore.swift`, language/speech settings stores | `src/Cotabby.App/Settings/` keeps autocomplete and learning values separate; learning uses its own DPAPI key and explicit endpoint opt-in; speech preferences are ported but their AppHost/provider/UI integration remains open | platform-adapted |
| Automatic update and release feed | `Cotabby/Services/Updates/` and settings models | No updater found; distribution is outside the first milestone | source-only |

## Verification policy

The learning corpus adds 64 cases for actual pinned Swift language/style/prompt/budget/output rules,
explicit-source filtering, confidence ranking and streamed-output bounds, also consumed by C# tests.
Recognition fixtures deliberately avoid invoking the statistical recognizer; they do not establish
multi-source detection equivalence. The corpus does not establish full learning parity, native
UIA/IME behavior or model quality; provenance and harness stubs are recorded in
`fixtures/parity/README.md`. ICU is the Windows sentence-tokenizer adapter, rather than a replacement
punctuation rule.

The local-learning corpus adds 45 checks against actual pinned Swift template resolution, GGUF
header parsing, delimiters, sampler recommendations, catalog and context policies. Fake native
sessions exercise the production token loop and ownership boundaries; synthetic headers and
in-memory HTTP handlers cover parser, file promotion and shutdown. These do not load weights or
prove native CPU/GPU inference. [Source/test mapping and gaps](LEARNING_LOCAL_RUNTIME.md).

The guarded editor insertion stage adds portable transaction, stale-result, cancellation and inline
query tests plus fake-driven Windows dispatch tests. It does not promote native focus, insertion,
clipboard or IME rows to verified. See [EDITOR_SAFETY.md](EDITOR_SAFETY.md) for the exact boundary.

The sense-content library adds 72 observations from the actual pinned Mac values, policy and escaping function: defaults, exact markup, Unicode meaningful-content behavior, UTF-8 text/caption limits, distinct block IDs, image metadata and repeated-reference aggregate budgets. These establish pure rule evidence only; notebook/store/media/review rows remain source-only. See [scope and source mapping](VOCABULARY_CONTENT.md).

The usage/capture library adds 63 actual pinned Mac observations for normalized entry identity, exact ordered usage receipts, capture validation and capture-to-explanation input/validation. It retains all usage metadata and stable UUID semantics as values; no saved notebook or native capture is implied. A shared Foundation range adapter preserves NSString CR/LF boundaries while visual text selection remains whole-grapheme. See [source mapping and gaps](VOCABULARY_CAPTURE.md).

The notebook/review graph now retains stable nested ownership, authorship, optional extensions and independent remote bindings as immutable values. Its 31 actual Mac probes cover defaults, optional/collection equality, entry/layout projections, card slots and pure binding merge. Save, database/codec/restart, template application, media and Anki transport remain absent. See [source mapping and evidence limits](VOCABULARY_MODELS.md).

Move a row to **verified** only with named evidence: shared fixture or unit-test output for portable
rules, plus native Windows host checks for behavior involving UIA, IME, input, caret rendering,
clipboard, insertion, persistent storage, or GPU runtime. Record the Windows version, architecture,
host apps and relevant hardware in the validation record. Do not promote a row based solely on file
existence or a green compile.
