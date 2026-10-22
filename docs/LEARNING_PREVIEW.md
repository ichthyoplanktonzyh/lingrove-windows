# macOS sentence-preview adaptation

This stage ports the manual, read-only sentence translation preview from stable macOS Lingrove at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. That implementation and its tests define the behavior;
the Windows files adapt the existing responsibilities to C#, WPF and Windows text segmentation.
The source reference is read through Git, independently of changes in the Mac working checkout.

## Source and responsibility mapping

| Stable Mac source / regression tests | Windows responsibility |
| --- | --- |
| `Models/LanguageLearning/LearningLanguage.swift`, `LearningLanguageTests.swift` | `Core/LanguageLearning/LearningLanguage.cs`: the same 14 durable language codes |
| `Models/LanguageLearning/LearningGenerationProfile.swift`, `LearningTranslationStyleTests.swift` | `LearningTranslationStyle.cs`: natural/formal/conversational/custom register, effective settings and bounded custom preference |
| `Models/LanguageLearning/LearningTranslationInput.swift` | Immutable current-sentence input; editor handles stay outside the engine |
| `Support/LanguageLearning/LearningExpressionPrompt.swift`, `LearningTranslationPrompt.swift`, matching prompt/style tests | Literal source as data, fixed translation instructions, generic/Hy-MT/TranslateGemma adapters and sampling |
| `Support/LanguageLearning/LearningTranslationBudget.swift`, `Support/Prompting/ModelContextBudget.swift`, budget tests | Reserve complete output, count full framing, reject oversized targets without shortening them |
| `Support/LanguageLearning/LearningTextScopePolicy.swift`, scope tests | Exact UTF-16/grapheme ranges and sentence-at-caret rules; native sentence tokenizer is a platform dependency |
| `Support/LanguageLearning/LearningSourceLanguageResolver.swift`, resolver tests | Ordered enabled-source filtering, explicit single-source preference, and deterministic confidence/margin policy; a Windows statistical recognizer remains unavailable |
| `Support/LanguageLearning/LearningInputMethodPolicy.swift`, input-method tests | Positive marked text rejects preview; unknown evidence stays unknown; no inferred composition state from timing |
| `Support/LanguageLearning/LearningExpressionOutputPolicy.swift`, output tests | The same complete-output validation and 12,000-byte UTF-8 limit used by the Mac translation service |
| `App/Coordinators/InlineFeatures/LearningSentencePreviewCoordinator.swift`, coordinator tests | A learning-owned request identity, captured settings, cancellation and latest-result guards; the App adapter retains field freshness |
| `Services/LanguageLearning/LearningTranslationService.swift` | Learning-specific endpoint generation, immutable destination/model, complete-result checks and task-owned credentials/client |
| `Services/Presentation/LearningSentencePanel.swift`, `UI/InlineFeatures/LearningSentenceCard.swift` | WPF read-only source/result panel with loading, failure, retry and cancellation |
| `App/Core/CotabbyAppEnvironment.swift` (`learningAvailability`), `Support/Suggestion/Request/SuggestionAvailabilityEvaluator.swift` | Shared app enable/pause/battery-saver and field/terminal eligibility; learning does not inherit autocomplete's integrated-terminal opt-in |

Mac paths above are relative to `Cotabby/`; named regression tests live under `CotabbyTests/` at the
matching responsibility. Windows production paths are under `src/Cotabby.Core/LanguageLearning/`,
`src/Cotabby.Inference/LanguageLearning/`, `src/Cotabby.App/Learning/` and `src/Cotabby.App/UI/Learning/`;
tests mirror these responsibilities under `tests/`. These mappings establish provenance, not a claim
of complete product or native-host parity.

## Preserved behavior

The feature starts disabled, with Chinese to English and natural style. A manually configured
shortcut captures the current sentence; inference receives only that bounded literal target and
the captured translation settings. There is no automatic clipboard, screenshot, draft or notebook
context. Style is a separate preference channel and cannot change the target range. Settings and
source changes invalidate older work, and a canceled or superseded reply cannot repopulate the card.
Global pause and configured battery-saver pause apply to learning too. Standalone and integrated
terminal fields remain ineligible even when autocomplete is enabled for integrated terminals.

The Mac context fallback is 8,192 tokens, with hard limits of 32,768 local / 131,072 endpoint tokens,
262,144 target UTF-8 bytes and 32,768 output tokens. The port preserves the allocation formulas,
specialist-model restrictions and 4,000-byte / 1,000-grapheme custom-style bounds. C# also rejects
malformed UTF-16 source because Swift strings cannot represent those invalid values.

## Necessary Windows adaptations and deferred behavior

- Mac `NLTokenizer` is replaced by Windows ICU sentence boundaries behind the same scope policy.
  It must not be replaced by a hand-written punctuation shortcut that splits abbreviations or
  decimals into partial targets. Locale/OS-version differences require native checks and explicit
  regression cases; no universal segmentation parity is asserted.
- UIA currently supplies a bounded field value and reliable collapsed-selection evidence. Unknown,
  secure, selected or truncated captures are refused for this current-sentence slice. The existing
  limits of 14,000 units before / 512 after the caret still apply. Full selected-text capture and
  absolute document-range anchors remain later migration work.
- Windows has no verified live marked-text provider in this stage. Pure Mac input-method rules
  are ported, while unknown evidence is never presented as proof that composition is clear. Native
  IME capture must still be validated before claiming committed-text safety across Windows hosts.
- WPF and Win32 replace the Mac nonactivating panel implementation. The original field, text,
  caret and focus epoch remain the capture authority; a late result cannot migrate to another field.
  Partial rendering uses cached focus evidence plus a 400 ms off-thread UIA freshness monitor;
  mouse-only host changes may therefore retire a provisional prefix with that polling delay. Native
  focus and selection timing remains a host-validation requirement, not a verified guarantee.
- Production routes only to the selected learning provider: a dedicated local CPU runtime or an
  explicitly opted-in OpenAI-compatible chat endpoint. Local is the fresh default with no model
  selected, and reports unavailable until the user imports or downloads and selects a file. Neither
  settings load nor backend switching prewarms a model. The endpoint retains its own URL/model,
  HTTP client and DPAPI credential. Autocomplete's runtime and credentials are never borrowed.
  The [local-runtime record](LEARNING_LOCAL_RUNTIME.md) maps templates, exact-token planning,
  model-file operations, persistence and remaining native validation to the Mac source.
- Preview is read-only in this round. Editor replacement, notebook, dictionaries, speech and
  automatic learning triggers remain explicit gaps; they are not replaced by new product flows.
- Streamed deltas now follow the Mac output policy: the first nonblank safe prefix publishes
  immediately, later prefixes coalesce to at most 30 updates per second, and completion always
  publishes a fully validated result. A prefix remains loading-only; failure, cancellation and stale
  work remove it rather than promoting it into a usable translation.
- Completed translations have the Mac 64-entry in-memory cache, with the same flush-all behavior
  before the next insertion at capacity. The key retains exact source, resolved direction, effective
  style/model/context and an opaque provider-profile scope. Translation configuration/credential or
  owner invalidation clears cached values; ordinary typing only retires the current capture. Cache
  hits still require current capture/settings checks before display. Local planning and exact token
  validation happen before cache lookup, so a cache hit can open the selected local model; its
  operation lease is then released without generation. Partial, failed and stale work
  never populate the cache, including a late completion after cache invalidation.
- Enabled source choices now use the Mac ordered-set semantics, retaining the legacy singular
  Windows setting when the new list is absent. An explicit empty list remains empty. Filtering out
  duplicates and the target can leave one explicit source; that direction needs no recognition.
  The Mac multi-source ranking policy is ported, but production recognition remains unavailable:
  multiple eligible sources fail closed with an explicit message. No rank or script heuristic is
  converted into a fabricated confidence. Windows ELS returns an ordered list rather than numeric
  candidate scores; the researched statistical alternatives do not establish the full Mac
  confidence/script-variant contract. This is an incomplete adaptation, not multi-source parity.
- Nearby-text language detection for sentence tokenization remains deferred and separate from
  translation-direction resolution. Windows supplies an explicitly configured locale to ICU;
  it does not claim to have detected the nearby language.

The recognition limitation is grounded in Microsoft's [language detection service contract](https://learn.microsoft.com/en-us/windows/win32/intl/microsoft-language-detection)
and [result structure](https://learn.microsoft.com/en-us/windows/win32/api/elscore/ns-elscore-mapping_data_range).
The pure resolver retains the pinned Mac thresholds and its injectable detector boundary so a
validated local provider can replace the explicit unavailable state without changing product rules.

## Validation boundary

Pure rules and lifecycle tests use immutable snapshots and fake engines. Endpoint tests use in-memory
HTTP handlers. Shared learning fixtures compare selected actual committed Swift rules with the C#
port, with the exact corpus scope and dependency stubs documented alongside them. Windows CI builds
the WPF integration and can test noninteractive ICU text segmentation. It does not exercise UIA,
IME, focus behavior or the rendered card on a real desktop, and does not run a model or contact a
translation service.

An interactive Windows environment is required for the host acceptance gate, not for implementing
these portable contracts. No virtual machine, installer, downloadable artifact, model-weight
download or release is created by this stage.
