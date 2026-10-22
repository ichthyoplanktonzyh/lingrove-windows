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
