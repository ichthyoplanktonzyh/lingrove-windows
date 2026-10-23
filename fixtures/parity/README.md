# Portable cross-language behavior fixtures

`v1.json` is a versioned, data-only corpus for deterministic inline-suggestion policies. Its
expected values come from the committed macOS baseline `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
not from the Windows implementation. `tests/Cotabby.Core.Tests/Parity/` runs these cases against the
Windows pure-policy types without loading a model, making a network request, or starting the desktop
app.

## Schema

- `schemaVersion` identifies the fixture shape; update it only when a field's meaning changes.
- `macBaselineRevision` pins the source snapshot used for the oracle.
- `acceptanceChunks` describes the visible tail, trailing-punctuation setting, exact accepted chunk,
  and `expectedUtf16Length`. The length is the accepted chunk's UTF-16 code-unit count, the indexing
  unit used by .NET strings and by serialized editor offsets.
- `ghostSpaceBoundaries` supplies the completion, current preceding text, request preceding text,
  and expected live-adjusted ghost. These cases all model the base-completion path where the request
  text is available.
- `normalization` supplies the request context and raw model output, plus expected normalized text
  and optional suppression reason. `prefixUtf16Length` is included for the emoji-prefix case so the
  fixture makes the cross-runtime cut length explicit; consumers should still pass the prefix string
  to the policy rather than slicing by that stored measurement.
- Each case carries `id` for failure output and `macTest` for the test establishing its rule.
  A derived probe need not be a literal input in that XCTest; such probes are identified below.

## Provenance

The acceptance expectations are covered by `CotabbyTests/Support/Suggestion/Acceptance/`:

- `SuggestionWordAcceptanceTests.test_nextAcceptanceChunk_includesLeadingWhitespaceAndNextVisibleToken`
- `SuggestionWordAcceptanceTests.test_nextAcceptanceChunk_bindsLeadingPunctuationToTheNextWord`
- `SuggestionWordAcceptanceTests.test_nextAcceptanceChunk_splitsTrailingPunctuationWhenAutoAcceptDisabled`
- `SuggestionPhraseAcceptanceTests.test_nextAcceptanceChunk_bindsTrailingIdeographicCommaToWord`
- `SuggestionPhraseAcceptanceTests.test_nextAcceptanceChunk_peelsLeadingCJKPunctuationRunInsteadOfSwallowingTheTail`
- `SuggestionPhraseAcceptanceTests.test_nextAcceptanceChunk_peelsLeadingCJKOpeningBracket`

The word-chunk mechanics live in `Cotabby/Support/Suggestion/Session/SuggestionSessionReconciler.swift`;
they operate on Swift `String.Index`/`Character` boundaries and keep CJK punctuation rules separate
from ICU's lexical segmentation.

The decomposed-accent fixture is a Unicode boundary probe derived from that `Character`-based
trimming path. Its cited XCTest pins the same trailing-punctuation rule with an ASCII word; the
committed Swift implementation establishes that the combining mark stays in the same `Character`.

Ghost spacing is covered by `CotabbyTests/Support/Suggestion/Output/GhostSpaceBoundaryTests.swift`.
Every fixture's `macTest` names the corresponding XCTest method. The policy is implemented in
`Cotabby/Support/Suggestion/Output/GhostSpaceBoundary.swift`.

Normalization is covered by `CotabbyTests/Support/Suggestion/Output/SuggestionTextNormalizerTests.swift`.
The emoji-prefix case is in `test_normalize_removesVisiblePrefixEcho`, and suppression expectations are
in `test_normalizeDetailed_attributesEverySuppression`. The production rule is
`Cotabby/Support/Suggestion/Output/SuggestionTextNormalizer.swift`.

The macOS repository has XCTest coverage for these policies but no native XCTest adapter that loads
this JSON corpus. The Windows repository provides an external verifier for the pinned Swift policy
code:

```bash
python3 scripts/verify-macos-parity.py --mac-repo /path/to/lingrove
```

It requires Swift 6.2 or newer and Xcode command-line tools. The verifier reads only the pinned
macOS commit with Git, extracts the pure policy sources into a temporary directory, maps fixture
requests through a narrow Swift DTO, and executes the same 17 cases. It does not edit the macOS
worktree or exercise Accessibility, the whole app, or native Windows behavior. The ordinary
`Cotabby.Core.Tests` run exercises the Windows policy implementation against the same JSON file.

## Extending and handling mismatches

Add cases only for deterministic behavior that can be traced to a committed macOS test or production
rule. Record the exact test name, keep each input minimal, and preserve Unicode text as UTF-8 JSON.
For any integer text offsets, state that the unit is UTF-16 and include a test assertion for it.
Review the same input against the pinned macOS source before accepting an expected value.

Do not assume an exact first word is portable for arbitrary unpunctuated CJK runs. macOS uses ICU
word segmentation, while the Windows core's default fallback is a script-run heuristic until its
application host installs a segmenter. The broad segmentation tests allow a non-empty prefix shorter
than the run. The `資料、` case here is a specific exception: the pinned macOS punctuation-binding
regression test asserts that exact chunk, and the same input passes the Swift verifier and C# runner.
The other two CJK cases stop at a leading punctuation character. New lexical-boundary mismatches need
a deliberate platform-adapter decision; changing expected output to whichever runtime ran it would
hide the divergence.

## Learning translation policy

`learning-v1.json` is a separate versioned corpus for deterministic learning rules ported in the
current Windows learning slice. Its expected values are checked against the pinned macOS commit
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` by the Swift-source verifier, then consumed by
`tests/Cotabby.Core.Tests/Parity/LearningParityCorpusTests.cs`:

```bash
python3 scripts/verify-learning-macos-parity.py --mac-repo /path/to/lingrove
```

The corpus records stable language codes, translation-style normalization and instructions, generic
and specialist prompt templates, token estimates, input/output allocation, whole-target rejection,
source-language score ranking and explicit-source filtering, streamed-prefix hygiene, and complete-
response validation. Long repetitive strings use small `repeat` or `concat` text specs; both test
runtimes expand those specs before invoking the policy. Each row has a macOS XCTest or an explicit
derived-probe reference so the rule and its source can be reviewed.

The verifier reads every source from Git at the pinned revision. It compiles the actual
`LearningTranslationStyle` declaration from `LearningGenerationProfile.swift`, plus the committed
language catalog, language values, token estimator, context budget, generic/specialist translation
prompts, prompt budget, `LearningSourceLanguageResolver`, output policy, and insertion-safety gate.
It extracts only the style type from the generation-profile file to avoid unrelated settings and
sampling-profile dependencies. Fixture-only stubs provide the text DTO consumed by the pure
prompt/budget functions, the model-template enum needed to type-check an unexercised initializer, a
sampling-value DTO, and an impossible-to-trigger endpoint response error. They supply no policy
behavior. The verifier also checks that the pinned `LearningTranslationService` still uses the
output policy for streamed prefixes and the completed response.

`sourceLanguageScoreCases` call the real Swift `LearningSourceLanguageResolver.confidentLanguage`
with fixed scores from its regression tests, including the confidence floor, margin, tie, and empty
map. `sourceLanguageResolutionCases` exercise the resolver's empty and explicit single-candidate
branches, including target filtering and duplicate settings. A guard in the Swift driver rejects any
case whose eligible set has multiple candidates, so these cases cannot accidentally consult
`NLLanguageRecognizer`. The Windows parity test injects a counting detector and asserts that the same
cases never call it. The multi-source operating-system recognizer's probability distribution is
platform- and OS-version-dependent; this corpus does not stub those probabilities or claim statistical
recognition parity.

The output boundary is part of the actual translation path: at the pinned commit,
`LearningTranslationService.generateValidated` accumulates partial translations with
`LearningExpressionOutputPolicy.Stream` and validates the final model response with
`LearningExpressionOutputPolicy.validatedText`. The policy rejects more than 12,000 UTF-8 bytes
before preview or insertion. `streamAppendCases` check whitespace-only prefixes, safe partial text,
control and replacement-character rejection, and the cumulative byte limit. The completed-output
cases check the same exact limit, edge trimming, and replacement-character rejection; the corpus
does not rely on the policy's separate use by structured hints.

The verifier uses `xcrun swiftc -swift-version 6` and temporary source files. It does not build the
macOS app, load a model, contact an endpoint, or access an editor. The existing test-project fixture
glob already copies all files below `fixtures/parity/`, so adding this corpus requires no extra
project-file item.

## Local learning runtime policy

learning-local-v1.json pins the local instruction-template and model-context behavior from the
same macOS baseline. tests/Cotabby.Core.Tests/Parity/LearningLocalParityCorpusTests.cs consumes the
expected values, and the independent source runner is:

~~~sh
python3 scripts/verify-learning-local-macos-parity.py --mac-repo /path/to/lingrove
~~~

The Swift runner compiles the complete pinned LearningLocalPrompt.swift,
LearningRuntimeCatalog.swift, LearningLocalModelConfiguration.swift, ModelContextBudget.swift, and
TokenCountEstimator.swift. It also extracts the actual LearningChatTemplate, LearningModelSnapshot,
and role/backend value declarations from LearningModelSettings.swift, plus the actual
LearningTranslationPrompt.Family and LearningInstructionSampling declarations. Those are pure
policy/value declarations; the @MainActor settings object and its Combine, UserDefaults, and
power-source dependencies stay outside the harness.

Only unrelated value shapes are stubbed: the app error wrapper, style and sampling-override values
used as stored fields, the sampler result DTO, and the download-catalog DTO. These stubs provide no
resolution, delimiter, budget, sampler-selection, or catalog behavior. For metadata cases the
driver writes a few-byte GGUF header containing only architecture/name/chat-template strings, using
the layout from LearningLocalPromptTests; the committed Mac file reader parses that header. No
model weights or native tokenizer are opened.

The cases cover stable template identifiers and role availability; automatic architecture,
filename, and embedded chat-template resolution; specialist task family and recommended sampler;
all committed delimiter pairs with literal payload preservation; TranslateGemma and full-context
accept/reject boundaries; exact catalog context recommendations; the Mac snapshot's 8,192-token
fallback value; and the shared 32,768-token local ceiling. Metadata resolution includes Hy-MT2's
two tokenizer formats despite their shared architecture and TranslateGemma identity after a rename.

The whole LearningModelSettings.snapshot computation is not executed: it also resolves persisted
per-model overrides, endpoint/local scope, power profiles, credentials, and observable settings.
The fixture therefore checks the pure catalog recommendation function and the actual snapshot
fallback value separately; it does not claim parity for persistence, power switching, native token
counts, GGUF tensor loading, or inference. The verifier reads all Swift source from Git at the pinned
revision, compiles with Swift 6, and uses temporary files only.

## Lexical recognition and platform text

`learning-lexical-v1.json` records 33 actual Mac observations at the same pinned revision:
13 regression adaptations and 21 explicitly marked source-policy extensions. Each row names its
production source; adapted rows also name a related Mac regression. Added query/hit probes are
observations of that source, not claims that every probe existed in the original test.

```sh
python3 scripts/verify-learning-lexical-macos-parity.py --mac-repo /path/to/lingrove
```

The runner compiles full committed `LearningUnitPolicy` and `LearningUnitRecognizer`, language and
catalog code, plus the exact pure word/unit/entry declarations sliced from their original files.
It executes actual `NLTokenizer` boundaries, index lookup, recognition, keys and hit testing. The
624 `facts` are domain keys and driver projections of Swift character predicates, not replacement
recognition code. The initial 33 expectations were captured before the C# recognizer; a further source-derived
casing regression was added after the first Windows CI exposed contextual Sigma behavior.
All existing expected values were retained unchanged. An
optional capture command writes a new observation file for explicit authoring; normal verification
never mutates expected results.

`tests/Shared/LearningLexicalCorpus.cs` is compiled only into tests. Its oracle answers recorded
word/fact lookups and contains no segmentation or casing algorithm; production uses the Windows
ICU adapter. Windows tests compare all native Unicode facts and 31 English/Spanish/French word/recognition cases.
`nativeWordParity` is a predeclared evidence scope: the mixed-script and CJK cases retain Mac word
observations without claiming that ICU globally reproduces NLTokenizer. See
[the scoped adaptation record](../../docs/LEARNING_LEXICAL_RULES.md) for ownership, mappings and gaps.

The verifier additionally checks all three bundled `resources/LearningLexicon/learning-expressions*`
files against their pinned Mac Git bytes. The complete actual Mac Codable resource model must
round-trip every entry, sense and attribution field without changing the JSON value. This integrity
check performs no source-data download or refresh; [the resource/store slice](../../docs/LEARNING_OFFLINE_LEXICON.md)
documents loading and platform evidence separately from lexical policy observations.

`learning-dictionary-ui-v1.json` adds 19 source-policy extensions captured from the actual Mac
`DictionaryLinkPolicy` (14 URL admission cases) and `LearningDictionaryViewport` (five measurement
sequences). The same Swift executable compiles both complete policies, plus their real content and
insertion-safety dependencies; no implementation is stubbed. Portable C# compares the committed
admission/heights observations. These are policy checks rather than WPF geometry/focus tests;
[the card adaptation record](../../docs/LEARNING_WORD_PREVIEW.md) defines that boundary.

## User-authored sense content

`vocabulary-content-v1.json` contains 72 observations from the same fixed Mac revision. The complete
content models (excluding backup), complete content policy, original VocabularyError enum and the
exact original ReviewCardPolicy.html function execute in a temporary Swift binary. The driver
constructs actual model values, validates before rendering, and projects validity, meaningfulness,
ordered image filenames and exact HTML. No exercised algorithm is stubbed.

```sh
python3 scripts/verify-vocabulary-content-macos-parity.py --mac-repo /path/to/lingrove
```

Short HTML is recorded directly; longer outputs record SHA-256 plus UTF-8 byte count. Repetitive
UTF-8 boundary inputs use a `repeating`/`count` text spec expanded by both test drivers. Defaults,
formatting/escaping order, Foundation whitespace (including U+200B), distinct IDs, 64-block limit,
image dimensions/digest/count, extreme 64-bit metadata and repeated-image aggregate are covered.
Regression adaptations cite the actual content test/method; added boundary probes explicitly use
`source-policy-extension`. Expected values were captured from actual Mac output and are never
updated by normal verification. This is pure data/HTML evidence, not archive, PNG, UI or Anki testing.

C# tests also reject null/unknown/malformed UTF-16 values that have no native Swift equivalent.
Windows RenderHtml validates before interpolation; Mac archive/review callers validate before html.
[The scope record](../../docs/VOCABULARY_CONTENT.md) lists the unfinished notebook/media boundaries.

## Captured usage identity and admission

`vocabulary-capture-v1.json` adds 63 source-policy probes from the same fixed Mac revision. The
verifier compiles complete meaning/example/capture declarations, the actual entry Kind declaration,
original normalization/key framing/capture validation/range validation methods, and the actual
capture-to-explanation input/validation methods. Unported sense/review/store graphs are omitted,
not replaced by fake algorithms. Original language/catalog/budget/estimator/error sources supply
their dependencies.

```sh
python3 scripts/verify-vocabulary-capture-macos-parity.py --mac-repo /path/to/lingrove
```

Observations retain normalized byte count/hash and short raw text, entry/example/capture keys,
capture validity, explanation validity/occurrence receipt and all projected input fields (literal fields
use exact UTF-8 lengths/hashes and short raw values; enums and ordered ranges retain their values). They cover language/kind/case,
NFC/Character whitespace, original UTF-8 budgets, exact occurrence/order/scope, annotation and
provenance independence, invalid/split ranges, empty/overlapping ranges permitted by the Mac
capture validator, and its NSString CR/LF range behavior. Expected values come from executing
Mac source; the .NET implementation does not supply an oracle. These cases establish no sentence
tokenizer, save/merge, notebook schema/restart, media or native editor behavior.

## Notebook/review graph values

`vocabulary-models-v1.json` adds 31 source-policy probes from the fixed Mac revision. The ninth
verifier compiles complete notebook/content/review/layout model declarations, original audio values
(excluding the speech-byte draft), explanation values and the exact original review merge function.
The driver only constructs deterministic values and observes actual defaults, optional/collection
equality, first-UUID explanation projections, ordered usage/layout slots and binding merge. It has
no C# replacement oracle and performs no archive or native app operation.

```sh
python3 scripts/verify-vocabulary-models-macos-parity.py --mac-repo /path/to/lingrove
```

The fixtures preserve canonical map/set equality, absent-versus-empty extensions, legacy/flexible
review coexistence, retained-plan traversal and endpoint/profile/origin/note deduplication. They
are pure value/projection evidence, not a complete field-by-field model codec roundtrip. Explicit
.NET snapshot mutation/hash and collection tests complement them. The [scope record](../../docs/VOCABULARY_MODELS.md)
keeps Save/merge transactions, storage, media, template application and Anki transport unfinished.

## Vocabulary row JSON and SQLite

`vocabulary-codec-v1.json` records 40 actual native inputs as base64 and the original pinned Mac
Payload encoder/decoder observations. It covers typed row leaves, source field/enum names, associated
content values, the 2001 numeric date epoch, duplicate canonical keys and byte-order handling.
[Codec source mapping and date/encoding limits](../../docs/VOCABULARY_PAYLOAD_CODEC.md) keep its scope explicit.

`vocabulary-sqlite-v1.json` adds 20 observations from the complete original Mac SQLite classes on
isolated in-memory databases. The new verifier compiles native SQL implementations, excluding only
two diagnostic log calls/import, with count assertions. It reuses the projection driver's fixed
Swift graph construction; the C# consumer constructs the same input graph from native codec bytes
and relational row inputs. SQLite expectations come from the separate Swift driver. These tests
exercise real SQL constraints, update/delete order and transaction rollback, not a fake database.
[SQLite scope and unresolved named-file protection](../../docs/VOCABULARY_SQLITE_TRANSACTIONS.md)
separate in-memory evidence from restart, disk durability, Windows file locks and product Save.

`vocabulary-archive-v1.json` adds 22 observations from the complete pinned Mac archive actor,
original image storage and actual memory SQLite. The driver only seeds faults/interleavings; it
does not substitute archive algorithms. Save/no-op, sense ownership, concurrent edits, semantic
admission, stale CAS, real SQL rollback and explanation authorship feed the actual C# archive.
[Archive ownership and outstanding media/product work](../../docs/VOCABULARY_ARCHIVE.md) describe
the limits. Normal verification never updates expected observations.

`vocabulary-store-v1.json` records 16 observations from exact pinned Mac Store declarations/method
bodies around the complete actual archive and SQLite. Its selected slice omits unported
CLI/media/review/export methods; only three diagnostic logging lines are removed under count guards.
Publication and concurrent pending-key probes use test scheduling hooks; mutations still execute
the original store/archive/policies. Expected values are captured only from this native driver.
[Store ownership and composition limits](../../docs/VOCABULARY_STORE.md) describe the Windows mapping.

`vocabulary-card-capture-v1.json` records 33 full capture observations from the original Mac
factory, including example translations, meaning languages and application provenance. Inputs and
NLTokenizer facts correspond to the earlier explanation-capture corpus; each old input projection
remained unchanged. Expected values come from native execution. Random encounter identity/time are
checked separately, not copied into expected JSON. [Writing-capture scope](../../docs/VOCABULARY_CARD_CAPTURE.md)
keeps reading/selection/OCR and UI work explicit.

`vocabulary-notebook-v1.json` records 51 actual pinned Mac notebook view/policy observations and
independent Foundation substring facts. The nineteenth verifier executes original search fields,
visible-entry/global-selection computed properties, sense titles and SwiftUI highlight runs.
Each case identifies the view, VocabularyPolicy and VocabularySensePolicy sources. Pure C# policy
consumers use explicit native facts; an additional 26 cases execute the actual Windows text adapter.
The three observed collation differences are explicitly locked by verifier ID; they are not native
successes. See [notebook scope and provider gaps](../../docs/VOCABULARY_NOTEBOOK.md).

`vocabulary-editor-v1.json` records 29 outcomes of actual pinned editor snippets: tag parsing,
source-text annotation retirement, both Save-disable predicates and stable-ID content movement.
The twentieth verifier compiles original closure bodies with a plain-value exposure wrapper;
only the State-to-value mutation qualifier changes. The original Swift equality condition supplies
onChange scheduling. Save cases evaluate source admission predicates, not a rendered SwiftUI button.
Expected values are independent of C#. [Editor mapping/evidence](../../docs/VOCABULARY_EDITOR.md)
keeps native UI, media and remaining notebook actions explicit.

`vocabulary-notebook-session-v1.json` adds 24 complete original Mac notebook/child session
observations. Configured fake generation is explicitly started, released and awaited in cancellation
cases; unchanged-input generation stays live at observation time. Actual provider arguments and
separately captured native word facts accompany explicit dictionary queries. Source routing/miss/error
fallback is outside this first-successful-hit scope. C# runs the same operations against the real
App session and Core children on a Windows dispatcher; the twenty-first Swift verifier checks the
original sessions. [Session scope](../../docs/VOCABULARY_NOTEBOOK_SESSION.md) keeps UI integration open.

`vocabulary-image-preparation-v1.json` adds ten complete original ImageIO prepare/validate
observations on fixed byte inputs (the input-size case is generated as equal repeated bytes).
Dimensions, rejection and returned-reference self-consistency are compared; codec PNG bytes are
platform-owned. The oversized-area header is also a malformed pixel payload and only establishes
rejection. EXIF/profile/multiframe/output-size Windows cases remain separate native-adapter
regressions. [Image preparation scope](../../docs/VOCABULARY_IMAGE_PREPARATION.md) leaves media/UI open.

`vocabulary-image-storage-v1.json` adds twelve observations from the complete original Mac
`VocabularyImageStorage.swift`: actual memory or temporary private file operations on fixed shared
PNG bytes, reuse/new-write flags, corrupt-file repair, missing/read removal, invalid-byte and
metadata rejection, directory/root-link/leaf-link admission and oversized-read rejection. The
verifier supplies only inputs/driver calls; it does not replace storage or expected behavior. Native
filesystem error types project as unavailable, and platform encoder bytes are not compared. Seven
additional Windows cases exercise ACL/identity/publish/cleanup ownership protections independently. Archive
transactions, backup/restore and editor image UI remain open.

`vocabulary-media-transactions-v1.json` adds twelve complete original Mac archive/media actor
observations with real memory SQLite and fixed PNG input. Its actual failing SQL trigger is test
setup, not a replacement transaction. It covers stage/commit/cleanup, missing and ignored bytes,
conflicts, pruning, budgets and original same-filename semantics. Nine separate Windows named-file
cases cover native rollback/repair/cancellation/cleanup/locks/revisions; no claim of UI or backup
integration follows from these observations.

`review-card-v1.json` adds 175 actual observations from complete original review content/layout,
static templates and JSON values. The native Swift input/output carrier preserves literal BOM and
decomposed scalars; no NSString/JSONSerialization bridge supplies text-policy inputs. Encoding
success measures the original JSONEncoder directly. Three extra Core cases cover detached/canonical
values, closed enum admission and display-reference validation without changing stored-block
identity rules. See [mapping and evidence limits](../../docs/REVIEW_CARD_MATERIAL.md). No review UI,
Anki connection, remote mutation, audio generation or media upload is established by these cases.

`anki-connect-v1.json` contains88 observations executed through the complete pinned Anki client
with an in-process URLProtocol plus its unchanged private encoder. It covers local endpoint
normalization, framing/byte budgets, synthetic status/envelope/UTF responses and error redaction.
Numbers include source edge cases and a fixed-seed100-value array. Exact raw strings are compared
for single-key bodies; multi-key request order is unspecified, with semantic values and exact
length compared. No network or real Anki is exercised; deep async Swift encoder stack behavior
remains an explicit limitation. See [scope and mapping](../../docs/ANKI_CONNECT_TRANSPORT.md).

`anki-fields-v1.json` contains76 observations from four exact original AnkiReviewPolicy methods
(fields, conflicts, removal and audio markup) plus preset constants and complete card/layout
dependencies. It preserves canonical field identity, override precedence and exact UTF-8 budgets.
Large generated values expose byte count and SHA-256 instead of repeated megabytes; all smaller
outputs compare exact strings/keys/array count. No server, controller transaction or audio
freshness is established. See [mapping and limits](../../docs/ANKI_FIELD_POLICY.md).

`speech-text-v1.json` contains47 original speech-text/audio-freshness observations, with eight
adaptations of four existing SpeechTextPolicyTests regressions. The complete speech-text enum and
exact original freshness/digest helpers execute with native JSON strings, byte/grapheme limits
and receipt hashes. It establishes no voice, playback, remote media or send transaction; see
[scope and mapping](../../docs/SPEECH_TEXT_POLICY.md).
