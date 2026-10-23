# Prepared vocabulary export values

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Models/Vocabulary/VocabularyExportModels.swift` (`VocabularyExportPackage`) and
`Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift` (`package`). The original format dispatch
is in `VocabularyExportFormatter.swift`. No Mac archive, session or UI body is substituted here.

`VocabularyExportPolicy.Package` now composes the reduced document, format dispatch, size guard,
preview and counts. It checks cancellation before touching the source, snapshots the immutable entry
references, renders with the captured options/labels, and returns one prepared value. Snapshotting
adapts Swift array value semantics: an `IReadOnlyList` could otherwise wrap a mutable .NET list.
Payload and counts use the same captured graph. TXT/CSV retain their loop cancellation checkpoints;
JSON has no in-render check. There is no invented post-render cancellation check in this pure factory.
The [archive snapshot](VOCABULARY_EXPORT_SNAPSHOT.md) keeps its separate check after collecting media and preparing the package.

Counts describe the input graph, not deduplicated expression-list lines: entry and sense counts
include all input entries/senses, and example count is zero when examples are off or TXT expression
list is selected. Otherwise every usage counts. CSV/JSON expressions style does not suppress examples.
The 64 MiB whole-export data ceiling remains separate from the archive media and ZIP ceilings.

`VocabularyExportPackage` owns encoded bytes, preview, immutable configuration, counts and a generated
ID. That ID identifies a preparation, not a vocabulary record; a later sheet compares it before Save.
`DataFilename` is `Lingrove-vocabulary.<txt|csv|json>`. `Filename` becomes `.zip` whenever the captured
configuration includes images, even if the current media map is empty.

The Mac value's data/metadata are `let` and its image map is `var` so the archive can attach captured
media after rendering. The Windows class adapts that value ownership through `WithImages`: it
returns a new media snapshot preserving the same preparation ID and private immutable data. Input
media arrays detach, and `CopyData`/`CopyImages` return defensive copies, matching the existing
prepared-image boundary. Returned arrays cannot mutate another package or its source. Replacing a
media snapshot replaces the entire map; it does not merge stale media. This model does not validate
filenames, admit image bytes, read files or enforce the archive's separate media budget.

`VocabularyExportFormatter.Render` now dispatches all three existing pure encoders. TXT alone
consumes a presentation-supplied label snapshot. This is the complete portable preparation library;
[archive selection/media capture](VOCABULARY_EXPORT_SNAPSHOT.md) and [ZIP bytes](VOCABULARY_EXPORT_ZIP.md)
now have their separate adapters. [Destination validation/writing](VOCABULARY_EXPORT_SAVE.md) now has its own adapter; [session](VOCABULARY_EXPORT_SESSION.md) and
[export-form](VOCABULARY_EXPORT_WINDOW.md) integration have separate evidence. A prepared package is not a full backup or an installer artifact.

Thirty-three probes execute the actual original Mac factory and value declarations. They compare
format, configuration, data SHA-256/count, complete preview, filenames, graph counts and empty media
for all formats. Cases include long multibyte previews, multiple examples, examples disabled,
TXT/CSV/JSON expressions style, images/content flags, initial cancellation on empty/nonempty graphs,
new preparation IDs and data/media copy ownership. Ownership probes name the original model source;
factory probes name the original policy. Four C# regressions additionally protect whole-map
replacement/read-only views, captured source/configuration, cancellation-before-enumeration and
label forwarding. Fixture bytes are literal test data and do not establish media admission.

```sh
python3 scripts/verify-vocabulary-export-package-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportPackage
```

The [public export form](VOCABULARY_EXPORT_WINDOW.md) connects commands and destination choice. Source deletion/media snapshot transactions
and ZIP bytes are covered by the linked adapters, rather than by these package-value probes.
The [Windows destination/atomic replacement adapter](VOCABULARY_EXPORT_SAVE.md) has separate evidence.
[Session guards](VOCABULARY_EXPORT_SESSION.md) and the export form have separate pinned-source and native evidence.
