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
- Each case carries `id` for failure output and `macTest` for its provenance.

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

Do not pin an exact first word for space-less CJK runs here. macOS uses ICU word segmentation, while
the Windows core's default fallback is a script-run heuristic until its application host installs a
segmenter. The macOS acceptance tests intentionally assert only that a segment is a non-empty prefix
shorter than the run. A mismatch in that area needs a deliberate platform-adapter decision; changing a
fixture expectation to match whichever runtime ran it would hide the divergence.
