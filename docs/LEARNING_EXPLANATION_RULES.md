# Explanation input, prompt and output rules

This port adapts the stable Mac explanation rules and a local provider at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The Windows app has no explanation trigger yet;
independent local app settings/runtime ownership is mapped in [the runtime note](LEARNING_EXPLANATION_RUNTIME.md).
Session/card wiring remains unported. Translation and autocomplete runtime
ownership are unchanged.

## Source mapping and ownership

| Pinned Mac source | Windows adaptation | Responsibility and data flow |
| --- | --- | --- |
| `Models/LanguageLearning/LearningExplanationModels.swift`: context, input, result | `Core/LanguageLearning/LearningExplanationModels.cs` | Immutable captured strings, ordered UTF-16 ranges, study language and target kind enter policy. Meaning and explanation leave parsing as separate complete fields. A local occurrence receipt participates in value identity but never enters a prompt. |
| `Models/Vocabulary/VocabularyModels.swift`: `Kind`, `VocabularyTextRange` | `Core/Vocabulary/VocabularyTextRange.cs` | Only granularity and occurrence primitives; no entry, sense, usage UUIDs or notebook store are implemented by these types. |
| `Support/LanguageLearning/LearningExplanationPolicy.swift`: input/result validation, prompt, parse | `Core/LanguageLearning/LearningExplanationPolicy.cs`, `.Prompt.cs` | Deterministic validation, bounded study-material prompt and complete-response parsing. No live editor access, IO, provider or UI dependency. |
| `Support/Vocabulary/VocabularyPolicy.swift`: `valid(range:in:)`; Foundation text/JSON primitives | `Core/LanguageLearning/LearningExplanationText.cs` | Original range validation, grapheme-expanded windows, Foundation trim set and JSONEncoder-compatible string quoting. |
| `Support/Prompting/ModelContextBudget.swift`, `TokenCountEstimator.swift`; `InsertionSafetyGate.swift` | Existing shared Core budget/estimator/insertion safety policies | Conservative input estimate and safe text checks; the local provider also enforces exact framed token limits. |
| `LearningFailure.swift`, `VocabularyError.invalidContent` | `LearningExplanationException.cs` | Fixed failure categories and English fallback messages, without captured/model text in errors. UI localization is deferred. |
| `LearningExplanationPolicy.supportsGeneralInstructions`, `RuntimeModelCatalog.isKnownBaseModel`, `LearningModelSettings.generalInstructionsAvailable` | `Core/LanguageLearning/LearningExplanationModelPolicy.cs` | Separate model capability from configured state; require local metadata even for a manually selected template. |
| `LearningExplanationService.generate`, `LearningModelController.validateGeneralInstructionModel/generate`, `LearningLocalRuntime.generate` | `Core/LanguageLearning/ILearningExplanationEngine.cs`, `Inference/LanguageLearning/LearningLocalExplanationEngine.cs`, existing local runtime | Single-request local provider: frozen input/configuration to actual framed token plan to EOG-complete, byte-bounded, parsed result. App service/cache/settings/card wiring remains separate. |
| `Support/Vocabulary/VocabularyCaptureFactory.swift`: writing `preview`, `unit(original:false)`, `component`; `VocabularyPolicy.example/exampleKey`; `LearningExplanationPolicy.input(capture)` | `Core/LanguageLearning/LearningExplanationInputFactory.cs` | Projects completed writing values into an explanation input, preserving ordered original occurrences and the captured context receipt. No durable capture/entry store is created. |
| `VocabularyPolicy.example` sentence tokenizer; `LearningUnitPolicy.words` | `ILearningEvidenceSentenceTokenizer`, `Win32/Text/IcuLearningEvidenceSentenceTokenizer.cs`, existing lexical adapter | Full sentence token extents and word facts enter the projection. Native facts are validated before trusting a containing sentence; failures remain content-free. |
| `Services/LanguageLearning/LearningExplanationService.swift`; `LearningModelSettings.swift`: snapshot; `LearningExplanationModels.swift`: record/protocol | `Core/LanguageLearning/LearningExplanationService.cs`, `LearningModelSnapshot.cs`, `LearningExplanationRecord.cs`, `ILearningExplanationGenerating.cs` | Library service borrows an app-owned facade factory, freezes effective profile/language, loads saved results before cache, retains bounded complete records and rejects retired work. No settings persistence, notebook store or app instance is introduced. |

The values have no process lifetime or service owner. A future card or notebook session will own an
input snapshot and hand it to an independent explanation provider. Policy returns a bounded prompt
or parsed result and never queries the currently focused app. Input range collections are detached;
value equality preserves Swift's canonical string equality without normalizing original text.
The existing lexical models now use the same internal `Text/CanonicalStringComparer.cs`; their
comparison behavior is unchanged.

## Preserved rules

- Target: nonblank after Foundation trim and at most 32,000 UTF-8 bytes. Dictionary form may be
  empty, but has the same raw byte cap. Context is at most 262,144 bytes. There must be 1–64 positive,
  bounded, grapheme-aligned ranges. Range order and duplicates are retained; validation does not
  invent a requirement that each quote equals the target.
- Word, expression/pattern and sentence targets retain their distinct exact Mac instructions.
  The response language is independent of the study language. Study material is JSON-quoted data;
  the local occurrence receipt is omitted. Prompt object key order is not significant.
- The input allowance reserves the full configured output and the common framing estimate. The
  entire target, dictionary form and all original occurrence quotes are retained. Only reference
  context may shrink, by the original binary search around the span containing all occurrences.
  A clipped sentence becomes a fragment; absent context stays absent. If even the empty-reference
  prompt cannot fit, generation is rejected. An empty-reference fallback can still contain all
  quotes when no reference window fits.
- Parsing checks the raw 24,000-byte cap before trimming. It may unwrap one outer fence with a
  standalone LF and requires one JSON object with both string fields. Unknown fields are ignored;
  duplicate keys use the first value, matching actual Mac JSONDecoder. One leading UTF-8 BOM is
  accepted; trailing BOM, malformed JSON, missing fields and unpaired JSON surrogates fail.
  The actual decoder's 512-container nesting limit is preserved.
- Parsed fields are trimmed, nonblank, safe to display, and at most 2,000/16,000 UTF-8 bytes.
  Validating an already-created result checks its **raw** byte sizes, whereas parsing checks its
  trimmed fields. No streaming prefix or incomplete response is treated as a complete explanation.

Foundation trim includes U+200B and retains U+FEFF. .NET's default trim and JSON escaping differ;
explicit primitives preserve the observed Mac rules and byte budgets, including slash, emoji,
controls and U+2028. Malformed .NET UTF-16 in model-facing text and undefined enum values have no
Swift value counterpart and fail validation. The local receipt is outside this validation contract.
Grapheme boundaries use .NET StringInfo; Unicode-version differences beyond
the shared cases remain an explicit platform limit, not a claim of universal Foundation parity.

## Evidence

`fixtures/parity/learning-explanation-v1.json` contains 68 observations captured from actual pinned
Mac methods before accepting the corresponding C# results. Eight cases adapt the existing
`CotabbyTests/Support/LanguageLearning/LearningExplanationPolicyTests.swift` regressions; the others
extend source-policy boundaries. They cover target scope, response language, repeated/discontinuous
quotes, reference clipping, UTF-8 caps, invalid ranges/graphemes, output safety, fences, duplicate
keys, BOM, decoder depth and JSON encoding costs. Five additional .NET model tests cover detached
values, canonical equality, named-field roundtrip, receipt exclusion and malformed UTF-16.

`scripts/verify-learning-explanation-macos-parity.py` compiles the actual selected Mac methods and
their actual language, range-validation, budget, safety and error dependencies. Slices omit capture
construction and model compatibility methods rather than stubbing their algorithms. The driver
uses Codable JSON values throughout: Foundation JSONSerialization's NSString bridge can strip a
leading U+FEFF, which would corrupt the oracle. Capture mode writes a new file only; normal CI
compares committed observations. Unexpected driver failures abort instead of becoming valid errors.

Prompts compare exact surrounding prose, semantic JSON payload, UTF-8 byte count, conservative
token estimate and allowance. They do not claim byte-for-byte serialized object order or identical
escape spelling. Error strings are the original policies' English fallback text in a standalone
executable without app localization resources, not evidence of localized Windows/Mac UI parity.
Core tests and the fifth Swift verifier run on CI without a model, endpoint or desktop.

Independent local validation for the initial text-rule checkpoint: Core Release 2,029/2,029; full Windows-targeting
Release solution compilation with zero warnings/errors; all five Mac verifiers passed. Commands
are recorded in `AGENTS.md`; the solution compile additionally uses `-p:EnableWindowsTargeting=true`.

## Explanation model eligibility

The local provider below consumes this library gate; app service/settings UI integration remains
unported. `SupportsGeneralInstructions` rejects the Mac's two exact known completion filenames,
the delimited `base` token, translation-specialist aliases and the three specialist templates.
`GeneralInstructionsAvailable` preserves the original three states: false for an excluded model,
null for an otherwise eligible local model without metadata, and true after metadata confirms it
or for an otherwise eligible endpoint selection. Empty endpoint names passing this capability rule
are still unconfigured; the future service must check name/configuration independently.

`ResolveLocalProfile` composes that gate with the existing Mac-backed template resolver. It always
resolves Automatic metadata for capability, even when the user selected manual framing. Renaming
a Hy-MT/TranslateGemma file or selecting ChatML does not turn a specialist into a tutor. Once
capability passes, the selected framing and independent configuration remain authoritative.
This method must consume the exclusive operation's metadata before tokenization/generation;
a detached filename precheck or cached UI status cannot authorize the later operation.

The extended fifth verifier compiles the actual Mac support/base predicates and settings property.
Only the property's preferences/Combine context is replaced by fixed fixture values, not its
algorithm. `learning-explanation-model-v1.json` records 52 original observations (11 adaptations of
existing model-policy regressions and 41 source-policy extensions, including eight output-setting
clamp/default cases extracted from actual Mac settings/service code). Core tests also compose real
metadata with the gate, and Windows tests feed three tiny synthetic GGUF headers through the actual
bounded reader before applying it. No weights, endpoint or native model is used. Text classification
uses the existing .NET grapheme primitives; the recorded Latin, combining-text, separator and number
cases are covered, while universal Swift Unicode classification/casing parity is not claimed.

Independent model-gate checkpoint validation: Core Release 2,083/2,083 and full Windows-targeting
Release solution compilation with zero warnings/errors; all five Mac verifiers passed again.
The three new Inference header/gate tests execute on Windows CI, not on the macOS desktop.

## Local explanation provider

`LearningLocalExplanationEngine` implements a real provider over `ILearningLocalRuntime`, with no
fake production output. The future app-owned explanation service supplies an **explanation-only**
runtime and owns a one-use facade. This slice does not instantiate it in AppHost, add explanation
settings or expose a UI trigger, and does not borrow the translation or autocomplete runtime.

The facade freezes the local selection and sampler overrides, renders the Mac explanation prompt,
acquires an exclusive lease, repeats the capability check against its actual metadata, and tokenizes
trusted framing separately from literal study material. Actual framed tokens plus the full output
reserve must fit the lesser of configured and native context capacity. The runtime must terminate
with EOG; reaching the token limit is a failure even when a prefix resembles valid JSON. Each
received delta counts toward the 24,000-byte explanation limit; the underlying native UTF-8 buffer
keeps the shared 48,000-byte bound, as on Mac. The facade adds no second streaming buffer, and only
a complete response can pass the parser. No provisional answer leaves this interface.
Invalid captured material retains precedence over model configuration failures. Output overrides
use the Mac 256–4,096 range, with the service's 1,024 default exposed by the shared settings helper.

The short-lived configuration adds `RequiresGeneralInstructions`, false by default for translation.
The explanation facade forces it true. The actual native session factory calls the bounded
`ReadMetadataForOperation` preflight before loading weights, even for a manual template, then runs
the Core gate. Runtime allocation identity includes this requirement, so an unchecked resident
allocation cannot bypass preflight. After admission the facade checks again with operation-owned
metadata. Translation retains its existing manual-template metadata bypass.
On Windows, a read handle denies write/delete sharing from preflight through native weights/mmap
lifetime, keeping header evidence and loaded bytes tied to one file. It closes after native cleanup;
loaded model files must be released before replacing/removing them. A tiny-file Windows test covers
the sharing boundary; successful actual LLamaSharp weight loading under it still needs a native host.

Generation and tokenization run on a worker. Facade disposal immediately retires its owner token,
waits for generation/admission and releases its lease on a worker, then joins cancellation callbacks.
Direct caller/owner flags reject stale output even when linked cancellation callbacks are delayed.
Disposal does not unload or dispose the app-owned runtime. Failed generation attempts release their
lease; an unsuccessful release stays owned for a disposal retry. Policy/cancellation callback
failures survive native wrapping; other native/foreign errors become fixed content-free messages.
No raw text, prompt or model path is logged by this facade.

Fake-lease Windows tests cover literal framing, sampler/configuration snapshots, renamed specialists,
actual context, raw/delta caps, invalid JSON, token truncation, callback error wrapping, late admission,
late answers, repeated disposal, release retry and blocked cancellation callbacks. Additional tests
exercise resident identity changes and the actual production preflight with tiny GGUF headers.
These are model-free boundary checks, not a native model execution or quality evaluation.

## Writing capture to input

`LearningExplanationInputFactory` ports the actual capture-factory → input composition for the
current manual caret-sentence **writing translation**. `FromWritingSentencePreview` requires a
completed nonempty source/result; `FromUnit` retains the Mac's separate guards and does not invent
the preview's nonempty-source requirement. The caller supplies an immutable presentation/unit;
the factory has no UIA, notebook, runtime or live focus state. A future session must separately
verify that its document is still current before showing a result.

One-word previews have no example. A multiword current-sentence preview has sentence target kind,
including an unpunctuated phrase, while its captured context can be a fragment if no single sentence
token contains the full range. Unit targets use the original surface for words/expressions and the
canonical query for patterns; dictionary form remains the query. Ranges keep their order and
duplicates. The containing sentence retains separator whitespace and rebases original UTF-16 spans;
without one containing sentence, the full text is a fragment. Standalone one-word translations lose
their example as on Mac. The input projection does not replace later `IsValid` checks.

Clicked components must be exact members of their unit. Their original occurrence maps through the
unit/example offset, with a grapheme/range/text check; repeated text is never found by searching for
the first match. The lowercase SHA-256 receipt uses the original Mac UTF-8 length framing over exact
example text, ordered `location:length` ranges and sentence/fragment completeness. Translation,
meaning, app provenance, UUIDs and provider identity do not affect that receipt or enter the prompt.

The new `ILearningEvidenceSentenceTokenizer` keeps full token extents, including leading/trailing
separator spaces. It reuses the pinned-buffer Windows ICU implementation but is deliberately a
separate interface from the existing trimmed current-caret sentence adapter. Both contracts remain
explicit. Core validates every returned sentence interval before selecting one, rejecting null,
overlapping, unordered, invalid or throwing providers with a fixed failure. This is an OS boundary
guard; it does not alter the Mac capture policy for valid token facts.

`learning-explanation-capture-v1.json` contains 33 observations from the actual pinned composition,
including six adaptations of `CotabbyTests/Support/Vocabulary/VocabularyCaptureFactoryTests.swift`
and 27 source-policy extensions: lifecycle rejection, standalone words, unpunctuated phrases, complete/fragment context,
separator spaces, surface/query differences, repeated/offset components and ordered duplicate
ranges. The sixth verifier compiles the actual capture factory, example/key policy, input methods,
models and NLTokenizer word/sentence dependencies. Its explicit lifecycle fixture projection maps
Windows non-ready statuses onto the Mac's stale/error/translating flags. It does not implement their
production state machine. Recorded token facts support portable C# tests only and cannot be
registered as a production provider. The Windows suite additionally exercises 31 explicitly scoped
English/French/Spanish capture cases through real ICU; other language/Unicode versions are open
platform limits. Fixed-error tests separately exercise malformed/throwing adapters.

This subset does not cover original-text toggles, selected text, reading/pointer/screenshot evidence,
`vocabularyContext`, notebook captures or durable entry/sense/usage records. Those flows require
their original Mac capture origin and corresponding adapters; the writing-preview method is not a
generic selection capture API.

## Library explanation service

`LearningExplanationService` adapts the original explicit service orchestration. The app
composition root now supplies an explanation-only local engine factory and committed profile/response-language
snapshots. Each generated record has its own UUID, frozen input, response language, complete parsed
result, model name and creation instant. Provider addresses and credentials do not enter a record.
The record is currently an in-memory value: no durable schema, Codable date compatibility, store,
notebook entry/sense/usage relationship or cross-platform storage compatibility is claimed.

Input validation precedes model configuration failures. Unless refresh was requested, an optional
asynchronous saved lookup (then synchronous fallback) wins over memory cache. Saved hooks are
composition-time notebook seams, unset in production today. Generated answers alone update memory
cache; refresh skips both saved/cache reads but replaces the complete cached record. Cache identity
is input + response language + complete effective model snapshot. Hits do not promote entries.
FIFO eviction retains at most 48 records and 4 MiB of the Mac's UTF-8 text estimate (target, context,
dictionary form, meaning, explanation), not a total managed-memory guarantee.

`LearningModelSnapshot.ForExplanation` mirrors the active-role projection: local clears dormant
endpoint URL/model/credential revision, endpoint clears the local name and uses Automatic, the
explanation role clears translation style, and endpoint sampling retains temperature/top-p only.
String equality/hash retain Swift canonical identity for profile strings and record model names;
original text is kept. Destination validation, model metadata and endpoint consent remain effect
boundaries; this projection cannot authorize IO. Power-source selection and per-destination settings
persistence still belong to the later settings adapter.
Snapshots must already contain installed model names and settings-normalized context/output
preferences. `ForExplanation` is the active-role field projection, not the complete settings parser,
backend context-limit clamp or power-profile resolver; generation still applies the output clamp.

The service checks caller cancellation, direct owner retirement, profile and language after saved
lookup and model work, and again under its cache admission lock. Settings changes retire pending
work before notifying future sessions. A one-use facade is drained before a record can be cached;
service disposal retires and drains generation and admitted saved lookups without disposing the
app-owned native runtime. Saved callbacks recheck language and closing state before returning.
Foreign provider/storage errors become fixed content-free failures. These are explicit Windows
thread/lifetime guards around the Mac orchestration, which runs on MainActor.
The future saved-store boundary also rejects a mismatched input/response language or invalid
result as a miss. Mac's notebook policy supplies trusted matching rows; this additional guard
keeps a corrupt/mis-keyed Windows adapter from bypassing validation. Unexpected foreign exceptions
use a fixed explanation-service failure rather than being mislabeled as local-model errors.

Provider implementations retain prompt/metadata validation, exact token budgeting, raw response
byte accounting, EOG completion and parsing; the service additionally checks parsed result validity.
The existing local engine enforces those boundaries. Endpoint names can pass the source capability
predicate, but no explanation endpoint or consent/credential path is implemented or wired. No new
network transmission is introduced by this service.

The tests adapt the independent-profile/cache/refresh regression in
`CotabbyTests/Services/LanguageLearning/LearningExplanationTests.swift` and add source/lifetime
boundaries: saved precedence, delayed preference/caller changes, FIFO/UTF-8 caps, invalid answers,
facade failure/disposal, saved lookup shutdown, dormant settings and canonical identity. They use
deterministic effect doubles, not a second Swift algorithm or real model. Existing six Swift
verifiers still cover their own committed pure-policy/capture cases; they do not execute this
asynchronous service. Request-ID trace/metrics correlation and display telemetry remain unported:
the Mac-shaped request ID and display hook are retained for that future integration, but generation
currently records no telemetry.

## Next adapter gates

Still unported: the remaining capture origins and notebook construction, explanation endpoint transport,
durable record ownership/persistence, request cancellation/stale-result session handling and the explanation
card. The [app-owned local provider](LEARNING_EXPLANATION_RUNTIME.md) has independent configuration/runtime
ownership; it reports unavailable rather than borrowing translation's selected model.
Native WPF/UIA/IME/model
execution and model-backed explanation quality are not established by these pure tests.
