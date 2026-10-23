# Export CSV-cell and UTF-8 preview boundaries

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift`, `csvCell` and `preview`.
This adapts two existing deterministic rules in `Cotabby.Core/Vocabulary/VocabularyExportFormatter.cs`.
The [TXT/CSV document renderer](VOCABULARY_EXPORT_NOTES.md) is a later connected library slice; JSON,
prepared packages, archive/file effects and export UI remain work.

The future renderer owns the reduced [portable document](VOCABULARY_EXPORT_PROJECTION.md). These
stateless helpers own only one CSV cell or a bounded byte preview; they neither query notebook state
nor create files, use clipboard services, run a model or transmit data.

CSV quotes every cell and doubles embedded quotes while retaining exact source text. When spreadsheet
compatibility is enabled, it skips Swift-equivalent whitespace/newline grapheme clusters and prefixes
an apostrophe before the original text when the first significant complete Character is `=`, `+`,
`-` or `@`. A marked/variation-selector grapheme with that base is not the literal prefix. Disabled
compatibility retains quoting but adds no apostrophe. No formula escaping applies to TXT/JSON.

Preview explicitly strips an initial CSV byte BOM, then takes at most 16,000 bytes. It trims until
the entire prefix is strict valid UTF-8 and appends LF plus `…` when the body is longer than that
prefix. This preserves complete encoded scalars; a combining sequence may be split between valid
scalars, exactly as on Mac. The marker is additional output beyond the 16,000-byte prefix.

Actual pinned observations exposed Foundation's separate decoding behavior: `String(data:encoding: .utf8)` consumes one leading UTF-8 BOM. .NET decoding preserves it, so this adapter removes precisely
one after successful decoding, independently of CSV's explicit removal. The original byte budget and
truncation comparison still count that decoded BOM. Thus TXT/JSON retain one of two BOMs; CSV retains
one of three. A BOM in an already-created CSV input string is literal, not whitespace.

Forty-six shared probes execute the actual pinned formatter methods. They cover quotes/newlines,
all four formula prefixes, Unicode separators, zero-width/control/combining/variation cases,
compatibility off, exact/over-limit UTF-8 boundaries, CJK, emoji, malformed/truncated bytes and BOMs.
The cell driver reconstructs inputs from integer Unicode scalars because Foundation's JSON string
reader can consume an initial BOM inside a field before the formatter sees it. The Python verifier
and C# consumer independently assert that those scalars equal the literal test input. No formatter
method is replaced; expected outputs are captured from the real source and then checked separately.

```sh
python3 scripts/verify-vocabulary-export-text-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportTextParityTests
```

These tests do not establish full CSV/TXT/JSON output, column/row selection, localized labels,
ISO date rendering, the 64 MiB whole-export limit, prepared packages, writing, ZIP or export-window
behavior. Those must use the same pinned Mac formatter and archive/session sources in later slices.
