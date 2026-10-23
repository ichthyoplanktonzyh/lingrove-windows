# Portable export JSON encoding

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Models/Vocabulary/VocabularyExportModels.swift`,
`Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift` (`render` JSON branch), and
`Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift` (`package` size guard).

`VocabularyExportFormatter.Json.cs` encodes the existing reduced [portable document](VOCABULARY_EXPORT_PROJECTION.md).
Its explicit field maps omit disabled optional values and exclude computed UI IDs/image filenames
that are not Mac Codable fields. Authored text, image metadata and opt-in explanations retain their
source shape. It does not use the storage row codec: that codec has different metadata, leaf shapes
and a 2001 date epoch, while this export uses ISO dates and selected learning material only.

A private closed-map writer adapts Foundation's exact encoding behavior: sorted ASCII field keys,
two-space pretty indentation, spaces around colons, the original empty-array blank lines, uppercase
UUIDs, UTC ISO dates without fractional seconds, literal Unicode and unescaped forward slashes.
Quotes, backslashes and required control characters are escaped; HTML-like text remains literal JSON
string data. There is no final newline. This is a fixed export shape, not a public arbitrary JSON
serializer or an input parser. The nested map depth comes only from that shape.

One call owns its maps and byte writer and returns detached UTF-8 bytes. The existing 64 MiB whole
export ceiling counts the final encoded bytes. Mac first completes JSONEncoder and then checks that
ceiling in `package`; Windows applies the same final data/error boundary while appending output, so
oversized JSON does not need a complete additional output allocation. This difference in check
timing does not change allowed payloads. There are no in-render cancellation checks, as in the Mac
JSON path; the [package factory](VOCABULARY_EXPORT_PACKAGE.md) keeps its separate initial cancellation check.

Nineteen fixtures execute the actual original JSONEncoder branch, with no replacement encoding
body. Smaller cases compare complete UTF-8 text and byte count, including optional omission,
empty arrays, styles/booleans, variants, empty image captions, image names, uppercase UUID letters,
positive/negative fractional dates, literal non-BMP/decomposed text, slash/HTML/control escaping and
64-bit integer metadata. The integer/control probes exercise typed encoding only; they do not claim
those values pass archive or image admission. Two large probes execute the original `package` method
at exactly 64 MiB and one byte over, comparing the native payload digest/count or `tooLarge` error.
They record `VocabularyExportPolicy.swift` as their provenance separately from encoder probes.

```sh
python3 scripts/verify-vocabulary-export-json-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportJsonParityTests
```

`RenderJson` sits beside TXT/CSV methods. Subsequent adapters provide [dispatch and prepared package
ownership](VOCABULARY_EXPORT_PACKAGE.md), [media snapshots](VOCABULARY_EXPORT_SNAPSHOT.md) and
[ZIP bytes](VOCABULARY_EXPORT_ZIP.md). Destination writing, full backup/restore and export UI remain
work. These JSON tests do not establish database compatibility or native desktop behavior, and
this encoder introduces no model, clipboard, filesystem or network effects.
