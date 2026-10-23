# Original review card material

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Complete source | Windows Core counterpart |
| --- | --- |
| `Models/Review/ReviewModels.swift` exported content value | `Review/ReviewCardContent.cs` |
| `Models/Review/ReviewLayout.swift` resolved parts/source labels | `Review/ReviewCardContent.cs`, existing layout/audio models |
| `Support/Review/ReviewCardPolicy.swift` | `Review/ReviewCardPolicy.cs` |
| `Support/Review/ReviewLayoutPolicy.swift` | existing `ReviewLayoutPolicy.cs` validation plus `.Resolution.cs` |
| `Support/Review/AnkiNoteTemplate.swift` | `Review/AnkiNoteTemplate.cs` |
| `Models/Review/AnkiWireModels.swift` JSON value declaration | `Review/AnkiValue.cs` (no connection/protocol yet) |

This round adapts existing deterministic rules. It adds no review UI, Anki client, settings/key
storage, server probe, speech generation, remote write or media upload. The notebook's existing
review plan/binding values remain unchanged.

## Ownership and content flow

A future review session owns its editable plan and supplies an acknowledged entry/sense/usage.
`ReviewCardPolicy` returns detached material for preview and later serialization. It reads no
Store, dictionary or model. Content equality compares exported text, ranges, blocks and optional
resolved sides using existing canonical-string/value collection helpers. Absence of flexible sides
remains different from empty sides. Audio stays a typed slot until rendering; it is not substituted
into arbitrary user strings.

Legacy preparation retains the original user-authored versus contextual answer preference, nil
versus explicitly empty answer, selected-example identity checks, content/notes/explanation flags
and gap/recognition admission. Gap ranges use the existing Foundation composed-range adapter,
including CR/LF behavior; invalid/overlapping/repeated/whole-sentence or sentence-kind gaps reject.
Sentence highlighting falls back to fully escaped text if any range is invalid. All literal markup
uses the original escape order.

Flexible resolution keeps its distinct meaning rule, exact source block/usage identity, final
record choice, missing-source errors, caption visibility and requested-side-only preview. Full
cards require both sides. Presets and template application retain original source order; portable
copies generate new part/custom-block IDs, clear source references, then application binds the
first matching destination text/image block. Copying never mutates the saved source/template.

One saved image may be referenced on both card sides. The internal occurrence renderer validates
each occurrence's text/image metadata before emitting markup/path, while preserving repetitions.
The public stored-block renderer still enforces unique block IDs and original aggregate budgets.
Display references are not treated as duplicate stored ownership or silently deduplicated.

## Templates and JSON values

Current and legacy templates/CSS are copied unchanged, including the current gap front that hides
meaning and audio. Multiline constants use explicit LF separators so a Windows CRLF checkout
cannot change exported CSS/template bytes. The exact HTML observations cover this contract; the
first Windows run exposed the raw-literal line-ending difference and was not accepted as passing.
Only known static templates are interpreted. Conditional processing precedes
one reverse-position field insertion pass, so braces inside field values remain literal. Preview
removes sound commands and supplies visible labels for flexible audio slots; it plays no audio.
Prepared field HTML is an input contract, not an arbitrary-HTML sanitizer or browser security model.

`AnkiValue` distinguishes bool, number, string, array, object and null and copies collections.
Its integer accessor follows original finite/positive/integral/strict-Int64-upper-bound rules.
Decoder duplicate-key precedence and canonical key equality retain the first key. Nonfinite
numbers cannot encode. Tests configure the JSON reader for original 512-container decoding;
the value converter retains the observed stricter encoding boundary. Generic caller limits may
be stricter; an HTTP framing/response-budget policy has not been introduced in this slice.
Future Anki consumers must select that 512-depth reader configuration; default System.Text.Json
settings accept less nesting. Source-slot labels currently retain English source strings; interface
localization remains a separate migration gap from Mac's localized labels.

## Actual-source evidence

`review-card-v1.json` contains 175 observations from complete original policies and values, executed
by `scripts/verify-review-card-macos-parity.py`. Inputs cover legacy answer/usage/origin flags,
Unicode ranges/highlighting, every flexible source/missing-source category, requested-side preview,
captions, repeated image references, part limits/identity, portable/applying layouts, current/legacy
templates and literal braces, bool/number distinction, large IDs, duplicate/canonical keys, BOM/
decomposed strings, nonfinite numbers and nested JSON boundaries. Only one gap case claims direct
adaptation of its original regression; other rows declare source-policy extension provenance.

The driver uses native Swift JSON values/JSONDecoder input and JSONEncoder output to preserve
leading BOM and decomposed scalars. An NSString/JSONSerialization bridge can consume a BOM before
the policy sees it, so that bridge is not an input oracle. Encoding success measures JSONEncoder
itself, separately from any observation projection. Complete source bodies are not rewritten;
existing dependency extraction retains its earlier scope and is not an implementation substitute.

Core consumes the same observations and adds three independent tests for immutable/canonical
content, undefined .NET enum admission, and repeated display references without relaxing public
storage/image-path admission. Source execution does not establish an Anki transaction, rendered
browser preview, audio output, media file identity or Windows desktop behavior. The CI adds its
forty-third actual-source verifier; no installer, build artifact upload, model download or network
test participates.

Local validation: the complete original Swift verifier passes all 175 cases; Core passes 3517 and
portable App passes 166, all zero failures/skips. Full Release solution cross-build has zero
warnings/errors. Windows CI must execute Inference539 and WPF497 plus the portable suites for the
final head; the full forty-three-verifier Mac job is also required before merge.
