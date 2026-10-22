# Explanation input, prompt and output rules

This slice adapts the stable Mac explanation **pure rules** at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. It does not yet expose explanation in the Windows
app or generate an answer. Translation and autocomplete runtime ownership are unchanged.

## Source mapping and ownership

| Pinned Mac source | Windows adaptation | Responsibility and data flow |
| --- | --- | --- |
| `Models/LanguageLearning/LearningExplanationModels.swift`: context, input, result | `Core/LanguageLearning/LearningExplanationModels.cs` | Immutable captured strings, ordered UTF-16 ranges, study language and target kind enter policy. Meaning and explanation leave parsing as separate complete fields. A local occurrence receipt participates in value identity but never enters a prompt. |
| `Models/Vocabulary/VocabularyModels.swift`: `Kind`, `VocabularyTextRange` | `Core/Vocabulary/VocabularyTextRange.cs` | Only granularity and occurrence primitives; no entry, sense, usage UUIDs or notebook store are implemented by these types. |
| `Support/LanguageLearning/LearningExplanationPolicy.swift`: input/result validation, prompt, parse | `Core/LanguageLearning/LearningExplanationPolicy.cs`, `.Prompt.cs` | Deterministic validation, bounded study-material prompt and complete-response parsing. No live editor access, IO, provider or UI dependency. |
| `Support/Vocabulary/VocabularyPolicy.swift`: `valid(range:in:)`; Foundation text/JSON primitives | `Core/LanguageLearning/LearningExplanationText.cs` | Original range validation, grapheme-expanded windows, Foundation trim set and JSONEncoder-compatible string quoting. |
| `Support/Prompting/ModelContextBudget.swift`, `TokenCountEstimator.swift`; `InsertionSafetyGate.swift` | Existing shared Core budget/estimator/insertion safety policies | Conservative input estimate and safe text checks; an eventual local adapter must also enforce exact framed token limits. |
| `LearningFailure.swift`, `VocabularyError.invalidContent` | `LearningExplanationException.cs` | Fixed failure categories and English fallback messages, without captured/model text in errors. UI localization is deferred. |

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
controls and U+2028. Malformed .NET UTF-16 and undefined enum values have no Swift value counterpart
and fail validation. Grapheme boundaries use .NET StringInfo; Unicode-version differences beyond
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

Independent local validation for this slice: Core Release 2,029/2,029; full Windows-targeting
Release solution compilation with zero warnings/errors; all five Mac verifiers passed. Commands
are recorded in `AGENTS.md`; the solution compile additionally uses `-p:EnableWindowsTargeting=true`.

## Next adapter gates

Still unported: capture-to-input and notebook construction, model instruction-compatibility gates,
independent explanation settings/runtime and endpoint transport, exact native token planning,
record ownership/persistence, request cancellation/stale-result session handling and the explanation
card. The translation model must not be borrowed to simulate this feature. Production will need a
real independently configured provider or an explicit unavailable state. Native WPF/UIA/IME/model
execution and model-backed explanation quality are not established by these pure tests.
