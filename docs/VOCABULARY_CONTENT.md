# Sense content values and rules

This stage ports the stable Mac user-authored sense-content boundary. It provides immutable
text/image block values, metadata validation, meaningful-content policy, ordered image projection
and literal review HTML. It is a library boundary; no notebook/store, save button, image import,
PNG decoder, backup, Anki connection or media write is connected yet.

The behavior reference remains `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Mac source | Windows responsibility |
| --- | --- |
| `Models/Vocabulary/VocabularyContentModels.swift`: block, text, image reference | `Core/Vocabulary/VocabularyContentModels.cs`: ordered immutable block values. Explicit creation generates a UUID; edits/copies keep it. Text formatting and image captions stay independent of model-generated explanation records. |
| `Support/Vocabulary/VocabularyContentPolicy.swift` | `Core/Vocabulary/VocabularyContentPolicy.cs`: pure budgets/validation, meaningful content, image occurrences and review projection. A future archive/editor/review owner supplies snapshots; the policy owns no lifetime, files or model. |
| `Support/Review/ReviewCardPolicy.swift`, `html(_:)` only | `Core/Review/ReviewHtmlText.cs`: exact literal escaping and LF conversion, shared by content and future review serializers. It is not an HTML sanitizer. |

The future sense model must retain optional content: absent differs from an explicitly empty block
list. No durable document or serialization/migration format is introduced here, and Mac Codable
or SQLite compatibility is not implied. Prepared image bytes and portable backup values remain with
the future media/archive stage. Review layouts/cards, entry/sense/usage identities and their storage
are separate unfinished boundaries.

Validation preserves Mac limits: at most 64 blocks with distinct IDs, text at most 262,144 UTF-8
bytes, captions at most 16,384, lowercase 64-character ASCII-hex image digest, dimensions 1–1,600,
image byte count 1–4 MiB and a 16 MiB aggregate per sense. Repeated image references count once per
block occurrence; physical file deduplication cannot bypass this budget. Numeric metadata stays
64-bit until validation, so imported extreme values reject before sums or allocation. Metadata
validity does not prove that PNG bytes, dimensions or a SHA-256 digest match.

Meaningful content follows Foundation's whitespace/newline trimming, including U+200B. It treats
any image as meaningful independently of metadata validity. It is not an admission check. Original
Unicode is never normalized in stored values or HTML; value equality follows Swift canonical
string equivalence. Text boundaries are UTF-8, not UTF-16 or grapheme counts.

The HTML projection preserves order, exact wrapper/styles, bold-then-italic nesting, one list per
bullet block, fixed image sizing, escaped alt/caption and optional figcaption. Escaping replaces
`&`, `<`, `>`, quotes and apostrophes, then LF with `<br>`; CR is retained. Windows `RenderHtml`
validates the complete input itself before interpolation, while Mac archive/review callers perform
validation before calling `html`. Malformed .NET UTF-16, null alternatives and unknown styles have
no Swift value counterpart and reject with a fixed recoverable error. No raw text/path is in it.

`fixtures/parity/vocabulary-content-v1.json` has 72 cases observed from actual pinned Mac source.
`scripts/verify-vocabulary-content-macos-parity.py` compiles the complete content policy and model
declarations (excluding backup), the original error enum and the exact original escaping function.
Its driver only constructs fixture values and projects results; no exercised policy is stubbed.
Long HTML observations use both exact UTF-8 byte count and SHA-256; short results additionally keep
readable exact strings. The C# corpus compares the same observations. New UTF-8/aggregate/whitespace
edge probes are labeled source-policy extensions; regression adaptations name their Mac test method.

The original `VocabularyContentTests.swift` also exercises archive transactions, reopen, conflict,
image preparation, backup repair/permissions and Anki front/back behavior. Those are not covered
by this pure slice. Standalone Windows tests cover stable block editing/order, detached repeated
image projection, canonical equality with raw preservation and fail-closed .NET-only invalid values.

Run:

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyContent'
python3 scripts/verify-vocabulary-content-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

CI runs both platforms' portable tests and the seventh pinned Swift verifier. It downloads no model,
creates no package and uploads no artifact. Passing these cases establishes the named pure rules;
it does not validate persistent data, native image decoding, a notebook UI or an Anki server.
