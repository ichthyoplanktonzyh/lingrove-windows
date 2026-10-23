# Stored export ZIP encoding

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Support/Vocabulary/VocabularyExportZIP.swift`, complete encoder and path check.
`VocabularyArchive.maximumBackupBytes` remains the original 192 MiB outer-file ceiling.

`VocabularyExportZip` is a deterministic Core adapter over one prepared package. The [archive save adapter](VOCABULARY_EXPORT_SAVE.md) calls it on its serialized worker, without reading the live notebook or media
again. This file owns the ZIP envelope only: it does not choose a destination, write files, decode
images, generate backups or publish an export command/window. PNG media is already compressed; the
original format uses stored entries with no compression service or subprocess.

The data entry comes first; image keys follow in ordinal order (all admitted names are ASCII).
The closed grammar admits only the three data filenames and `images/lg-image-<64 lowercase hex>.png`.
No directory, absolute path, alternate stream, traversal, Unicode/uppercase digest or arbitrary
attachment name is accepted. As in Mac, a direct encoder call includes the package's attached image
map even when its configuration disables images. The archive save adapter chooses whether to invoke
ZIP from `IncludesImages`; this encoder does not silently filter or repair that captured map.
Image values here are opaque bytes: path admission alone proves no PNG/reference/hash validity.
The archive snapshot/image owner supplies that separate guarantee.

At most 10,000 files and 192 MiB aggregate payload enter the encoder. These checks precede the first
cancellation check, then each file gets a cancellation checkpoint. Header bytes also count toward
the final 192 MiB limit. The byte format preserves Mac's local/central/EOCD records, version 20,
zero flags/method/time, DOS date 1980-01-01, unsigned IEEE CRC-32, offsets and little-endian fields.
There are no extras/comments or timestamps derived from the clock.

Only trusted Core code borrows internal read-only memory views of package-owned bytes. This avoids
another whole-media copy before ZIP budget admission; public callers still receive defensive copies.
Exact header capacity avoids geometric growth when the maximum file count and near-limit payload
are combined. The returned ZIP owns its result. Compared with Swift's copy-on-write values, C# package preparation
and final `MemoryStream.ToArray` can temporarily retain additional bounded copies. Budget admission
limits file size, rather than promising peak allocation equals that size.

Thirty-six fixture probes compile the complete original Mac archive/export declarations and call
the actual ZIP implementation. None invokes destination writing or image decoding. Small fixtures
store full ZIP bytes plus SHA/count; the 10,000-file case and four near-192-MiB cases and a combined maximum-file/payload case store SHA/count
or the actual fixed error, without committing large binary data. The exact final-limit case contains
201,326,592 bytes; adding one payload byte fails. The aggregate-input limit can also pass while the
headers push the final file over its limit. Invalid path/count admission retains its precedence over
cancellation. Configuration and sorted attachment cases retain the original encoder responsibility.

Two C# regressions additionally use the independent .NET `ZipArchive` reader to recover exact data
and image bytes, inspect the known CRC of `123456789`, stored method, flags and fixed date, and verify
repeat encoding/result mutation cannot alter the package or another ZIP result. The large cases run
in a nonparallel collection to avoid multiplying memory with other formatter budget tests.

```sh
python3 scripts/verify-vocabulary-export-zip-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportZip
```

[Atomic destination admission/writing](VOCABULARY_EXPORT_SAVE.md) has a separate adapter. Store/session
callbacks, stale save-session guards, the export window and full backup/restore remain work. Windows desktop UIA/IME/overlay/insertion and
model/GPU gates are unchanged by this portable byte-format slice.
