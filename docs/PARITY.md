# Feature Parity and Evidence

This matrix starts from the Windows fork baseline `b1e4031c70aca1be21aae2c57b138fde5fc22bb1` and
compares it with the committed macOS reference `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. It also
records the initial foundation branch's shared JSON corpus and its test evidence. The source paths
are reference points, not a claim that every Windows behavior has been validated. The Windows tree
still uses Cotabby names. The current learning addition is the scoped manual read-only preview
recorded in [LEARNING_PREVIEW.md](LEARNING_PREVIEW.md); the wider learning product remains incomplete.

## Status meanings

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
| Sentence explanation and offline lexical word/expression recognition | `Cotabby/Services/LanguageLearning/LearningExplanationService.swift`, `LearningExpressionService+Sentence.swift`, `Cotabby/Support/LanguageLearning/LearningExplanationPolicy.swift`, `LearningUnitRecognizer.swift`, `VocabularyCaptureFactory.swift`, matching policy/recognizer/capture tests | Completed translations receive independent lexical units and bundled double-click lookup; 34 lexical +19 link/viewport Mac cases. Explanation rules have 68 Mac cases, capability/output gates another 52, writing capture-to-input another 33; a local library provider joins metadata/exact-token/EOG/JSON checks. A library service adds frozen profile/language identity, saved-first lookup and a bounded complete-record cache. AppHost now owns independent local settings/runtime and the on-demand/60-second idle policy; a portable session handles saved restoration/refresh/cancellation. The writing sentence card now connects explicit explanation/stop/refresh with captured-document checks; native interaction/model execution are unverified. The unit/component WPF card now connects contextual meaning, exact examples and lazy dictionary navigation; [mouse/context selection Copy](LEARNING_TEXT_SELECTION.md) now shares captured-document/target grants; keyboard/word-double-click/autoscroll selection, telemetry, endpoint and source-word fallback remain open; [lexical scope](LEARNING_LEXICAL_RULES.md), [explanation scope](LEARNING_EXPLANATION_RULES.md) | implemented-unverified |
| Offline lexicon and system dictionary lookup | `Cotabby/Services/LanguageLearning/LearningLexiconService.swift`, `SystemDictionaryArticleService.swift`, `CotabbyTests/Services/LanguageLearning/LearningLexiconServiceTests.swift` | Pinned resource, bounded decoder/source, shared lazy store and bundled target-translation card adapted. The [contextual session library](LEARNING_CONTEXTUAL_QUERY.md) adds contextual default, lazy dictionary pages and target lifetime, now connected by the [WPF adapter](LEARNING_CONTEXTUAL_CARD.md). System/imported/online providers remain unported; [mapping and evidence](LEARNING_OFFLINE_LEXICON.md) | implemented-unverified |
| Imported MDX/MDD dictionaries and dictionary website providers | `Cotabby/Services/LanguageLearning/MDictLibraryService.swift`, `LearningDictionaryRouter.swift`, `OnlineDictionaryClient.swift`, `CotabbyTests/Services/LanguageLearning/DictionaryProviderTests.swift` | No dictionary subsystem | source-only |
| Stable vocabulary, senses, usages, notes and exact spans | `Cotabby/Models/Vocabulary/VocabularyModels.swift`, `VocabularyContentModels.swift`, `Cotabby/Services/Vocabulary/VocabularyStore.swift`, `CotabbyTests/Services/Vocabulary/VocabularySenseTests.swift` | Pure [sense-content values/rules](VOCABULARY_CONTENT.md) ported with 72 actual Mac fixture cases; usage/capture values and exact identity/admission have 63 actual Mac probes; entry/sense/review in-memory graph has 31 actual Mac model/projection/merge probes; pure [save/sense ownership rules](VOCABULARY_SENSE_RULES.md) add 33 actual Mac mutation probes; [whole-graph admission](VOCABULARY_ARCHIVE_VALIDATION.md) adds 124 actual Mac archive/layout/receipt cases; [concurrent edit/pruning drafts](VOCABULARY_ARCHIVE_DRAFTS.md) add 24 actual Mac helper cases; [typed relational projection](VOCABULARY_RELATIONAL_PROJECTION.md) adds 26 actual Mac mapping/reconstruction cases; [row JSON encoding/decoding](VOCABULARY_PAYLOAD_CODEC.md) adds 40 actual Mac leaf/decoder probes; [SQLite in-memory transactions](VOCABULARY_SQLITE_TRANSACTIONS.md) add 20 actual Mac native-SQL probes; the [Windows file adapter](VOCABULARY_WINDOWS_FILES.md) adds 37 native file/SQLite cases; the [serialized archive](VOCABULARY_ARCHIVE.md) adds 22 complete original Mac actor observations, six portable lifetime tests and two Windows archive/file cases; AppHost publication and Save UI absent | source-only |
| Rich notebook, images, graph and export | `Cotabby/Services/Vocabulary/VocabularyNotebookSession.swift`, `VocabularyImageStorage.swift`, `VocabularyGraphLayoutWorker.swift`, `VocabularyArchive+Export.swift`, `CotabbyTests/UI/Vocabulary/` | Pure text/image-reference validation and literal review HTML library only; no notebook, image preparation/storage, graph or export product | source-only |
| Anki review, card/media handling and connection | `Cotabby/Services/Review/AnkiConnectClient.swift`, `AnkiReviewSession.swift`, `Cotabby/Models/Review/AnkiWireModels.swift`, `CotabbyTests/Services/Review/AnkiReviewTests.swift` | Durable review/layout/audio-receipt values and pure binding merge ported; no Anki connection, send, conflict or media integration | source-only |
| System, local and endpoint text-to-speech | `Cotabby/Services/Speech/`, `Cotabby/Models/Speech/`, `CotabbyTests/Services/Speech/` | No speech subsystem | source-only |

Vocabulary data needs a deliberate schema. In the reference, a vocabulary entry has a stable UUID;
each sense has its own stable UUID and owns meanings, notes, examples, and review state; each usage
example has its own UUID and exact source ranges. Rewording a sense does not change its identity, and
review-card identity stays independent. Windows migrations must preserve these relationships. This
does not imply that Windows storage is already compatible with the macOS database.

## Supporting product capabilities

| Capability | macOS reference at pinned commit (source / tests) | Windows baseline evidence | Status |
| --- | --- | --- | --- |
| Typing history, privacy controls and phrase prediction | `Cotabby/Services/History/`, `Cotabby/Services/Runtime/TypingHistoryPhraseEngine.swift`, matching `CotabbyTests/Services/History/` | Suggestion metrics exist, but no typing-history store/index/phrase engine found | source-only |
| Suggestion/runtime/system metrics | `Cotabby/Models/Runtime/Metrics/`, `Cotabby/Models/Suggestion/SuggestionQualityMetricsStore.swift`, runtime tests | `src/Cotabby.Core/Suggestions/SuggestionMetrics.cs`, `tests/Cotabby.Core.Tests/Suggestions/SuggestionMetricsTests.cs` | implemented-unverified |
| Shared JSON behavior corpus (17 pure-policy cases) | `Cotabby/Support/Suggestion/Acceptance/`, `Cotabby/Support/Suggestion/Output/GhostSpaceBoundary.swift`, `SuggestionTextNormalizer.swift`; pinned tests recorded per case in `fixtures/parity/README.md` | `fixtures/parity/v1.json`, `fixtures/parity/README.md`, `tests/Cotabby.Core.Tests/Parity/VersionedParityCorpusTests.cs`, and `scripts/verify-macos-parity.py` check acceptance chunks, ghost spacing, and normalization in both runtimes | verified |
| Vocabulary CLI and mailbox protocol | `Cotabby/Services/CLI/`, `Cotabby/Models/CLI/`, `CotabbyTests/Services/CLI/VocabularyCLITests.swift` | No vocabulary CLI or mailbox | source-only |
| Interface localization and learning/settings search | `Cotabby/Models/Settings/InterfaceLanguage.swift`, `Cotabby/Support/Settings/InterfaceLanguageNormalizer.swift`, settings views/tests | WPF settings exist, but no interface-language system found | source-only |
| Durable task-specific settings | `Cotabby/Models/Settings/`, `Cotabby/Support/Settings/SuggestionSettingsStore.swift`, language/speech settings stores | `src/Cotabby.App/Settings/` keeps autocomplete and learning values separate; learning uses its own DPAPI key and explicit endpoint opt-in; speech settings remain absent | platform-adapted |
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
