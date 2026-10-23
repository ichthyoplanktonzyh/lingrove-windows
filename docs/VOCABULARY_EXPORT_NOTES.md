# TXT and CSV document rendering

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Support/Vocabulary/VocabularyExportFormatter.swift` (`text`, `appendUsage`, `csv`,
`csvColumns`, `csvValues` and their formatting helpers). The matching original export tests cover
Unicode/quotes/newlines, senses without examples, option omission and literal expression lists.

This adapts those deterministic render paths in `VocabularyExportFormatter.Notes.cs`, over the
existing [portable material document](VOCABULARY_EXPORT_PROJECTION.md) and
[CSV/preview helpers](VOCABULARY_EXPORT_TEXT.md). The explicit library APIs are `RenderText` and
`RenderCsv`. Separate adapters now cover [JSON rendering](VOCABULARY_EXPORT_JSON.md),
[format dispatch/packages](VOCABULARY_EXPORT_PACKAGE.md), [media snapshots](VOCABULARY_EXPORT_SNAPSHOT.md)
and [ZIP bytes](VOCABULARY_EXPORT_ZIP.md). [Destination writing](VOCABULARY_EXPORT_SAVE.md) has a separate adapter; the export window remains work;
these renderer methods are not connected to a product command.

Each call owns one bounded byte writer. It checks the exact UTF-8 byte count before encoding and
appending each source-equivalent chunk; headers, BOM, delimiters and newlines count toward 64 MiB.
It returns detached owned bytes and disposes its buffer on success, cancellation or failure. There
are no storage reads, filesystem effects, clipboard access, model calls or network requests.

TXT notes retain literal source text, language tags, dictionary-form difference under canonical
comparison, exact sense/example order, optional notes/tags, supplemental text/image names and
opt-in explanations. Blank lines and the separator match the Mac formatter. Empty primary/example
text suppresses its source-equivalent labeled line; empty authored text still adds LF. TXT expression
lists normalize with the existing Mac vocabulary normalization and deduplicate language + normalized
expression, preserving case and first encounter order. Their line count is not a count of senses.

`VocabularyExportTextLabels` is one immutable presentation-supplied snapshot of the existing Mac
Vocabulary-table labels. Defaults are its English source fallback strings, used by the source
verifier without app resources. A future Windows window supplies localized values at the platform
boundary. Rendering does not fetch UI resources or change its labels midway through output; app
localization and locale-resource parity are not claimed by this library slice.

CSV preserves fixed column-group order and uses the options to include meanings, examples, notes,
explanations and content. It emits one row per enabled usage, or exactly one row for a sense with
none; disabled examples never duplicate rows. UUIDs use Mac spelling, dates use second-precision
UTC ISO 8601, and every cell is quoted through the existing helper. Rows end in CRLF; spreadsheet
compatibility controls the BOM/formula prefix. CSV's expressions style still emits ordinary rows.
Text blocks, filenames and captions form separate ordered content columns. Tags join with `; `.

Cancellation checks retain Mac granularity: TXT per entry, sense and usage; CSV per emitted row.
A directly called renderer with an empty document performs no loop cancellation check (CSV still
returns its header). The [package factory](VOCABULARY_EXPORT_PACKAGE.md) preserves its separate initial cancellation check.

Forty-two fixtures execute the complete actual pinned Swift formatter and compare complete UTF-8
text plus byte counts. They cover options, no/multiple examples, quote/newline/Unicode values,
canonical dictionary forms, expression normalization/language dedup, supplemental/empty content, positive
and negative fractional dates, and cancelled empty/nonempty loops. Four actual-render probes accept
exactly 64 MiB and reject one byte over; the large successful output is represented by byte count and
SHA-256 rather than committing 64 MiB of fixture data. A multiple-usage case with examples disabled protects the original Mac row-deduplication regression.
Two C# regressions additionally exercise all
injected labels and culture independence for UUID/date/sense-number formatting. No fake formatter
body or archive is substituted. The real Swift entry/sense/usage input declarations are unchanged.

```sh
python3 scripts/verify-vocabulary-export-notes-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportNotes
```

The exact large limit is exercised through both TXT and CSV, with the CSV BOM/header included;
small complete-output cases also check each ordered row and its byte count. No export destination, prepared
package identity, atomic write, ZIP, full backup/restore, JSON bytes or export-window behavior is
verified by this slice. Existing native UIA/IME/overlay/insertion/model/GPU gates remain open.
