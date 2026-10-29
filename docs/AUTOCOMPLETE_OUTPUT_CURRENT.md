# Latest autocomplete output cleanup

Reference: committed Mac `06bf81fe52f22fd62e76d8ec0bfd898d3417158b`,
`Lingrove/Support/Suggestion/Output/SuggestionTextNormalizer.swift`,
`PromptPrefaceEchoFilter.swift`, `GhostSpaceBoundary.swift` and
`Lingrove/Support/Suggestion/Request/CaretWordContext.swift`. Regression expectations are read
from the corresponding `LingroveTests/Support/Suggestion/Output/SuggestionTextNormalizerTests.swift`.
This is a module upgrade to the latest recorded product, not a change to model prompts or sampling.

## Behavior and ownership

The pure `SuggestionTextNormalizer` is borrowed by generation callers. It owns no task, service,
window, model or persistent state. It receives raw output and the frozen request and returns usable
ghost text or the existing suppression reason. The latest Mac rule carries evidence of a removed
leading newline through formatting, prompt and label cleanup, then decides spacing at the surviving
seam after echo removal. This turns `Hello` plus `\nworld` into `Hello world` rather than
`Helloworld`. A prompt newline before an echoed `sched` does not turn its remaining `ule` into a
new word. Prefix-owned indentation survives until the exact prefix is removed.

The existing caret script predicate and ghost punctuation predicate are shared internally. A removed
delimiter does not insert a space into Japanese/CJK or after an opening bracket, duplicate a field's
whitespace, or alter ordinary mid-word/numeric continuations. Multiline formatting keeps the original
behavior. Optional backend-specific prompt echoes follow the same pure cleanup, without adding a
provider or changing a request's destination.

`PromptPrefaceEchoFilter` owns the original distinctive preface phrases and line-only openers. The
normalizer calls it before line collapse, keeping useful continuation text before an echoed preface.
Its separate file keeps prompt wording out of coordinator and backend state. Case-sensitive matches,
ordinary mid-sentence prose and trailing-whitespace handling follow the source helper.

## Validation scope

Four grouped regression tests adapt the actual Mac newline/script/punctuation, mixed-formatting,
prompt/label, indented-prefix, cumulative partial and preface cases. They do not expand the original
prefix/separator/suffix cross-product into dozens of separately discovered tests. Two older unit
expectations are explicitly advanced to the source's new leading-newline behavior. The historical
shared `fixtures/parity/v1.json` and its source revision remain byte-for-byte unchanged.

The focused output/caret/ghost/historical-corpus run passes128 tests. Full portable Core4604 and
App1222 tests pass with no failure/skip; the Release Windows-targeting solution builds with zero
warnings/errors. Independent read-only source review finds no blocker. Final-head CI is required
before merge. This pure output adaptation does not establish native host insertion, ghost geometry, model
quality or latest parity of the rest of autocomplete. No prompt content, context budget, sampling,
transmission, model download, installer or release is added.
