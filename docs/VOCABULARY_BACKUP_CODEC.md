# Full-notebook backup value and Codable boundary

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Models/Vocabulary/VocabularyModels.swift` (`VocabularyDocument`) and
`Cotabby/Models/Vocabulary/VocabularyContentModels.swift` (`VocabularyBackup`).
This slice ports their complete portable representation. It does not yet connect backup or restore
to archive IO, Store or notebook commands.

`VocabularyBackup` owns the version, an immutable full document and copied media bytes. Public media
access returns another detached map/arrays; only the synchronous trusted codec borrows private bytes.
This differs from learning-material export: the full graph keeps stable entry/sense/usage IDs,
authored content, explanations/provenance, legacy and independent review cards, preserved plans,
Anki bindings/receipts and optional review templates. Nothing is flattened into a translation string
or projected through stripped database row headers. Canonically duplicate constructor map keys are
rejected consistently with other immutable graph maps.

`VocabularyBackupCodec` encodes/decodes the document alone or the versioned envelope. Its internal
Wire type exposes exactly `version`, `document`, `images`; images use JSON base64, not live file paths.
The existing `VocabularyPayloadCodec` supplies the same required/optional fields, source names,
uppercase UUIDs, numeric 2001 Date epoch, enum-key dictionary arrays, content unions, strict UTF input
and first duplicate-key behavior already used by row leaves. Narrow internal generic calls share
those rules without sharing relational projection. All malformed decode failures remain fixed
InvalidArchive errors and contain no literal input. The envelope's version and document schema
marker now use C# long, matching the reference platform's 64-bit Swift Int before version admission.
SQLite's separate on-disk schema marker is unchanged.

Two additional leaf adapters preserve observed Foundation behavior. Base64 rejects whitespace that
.NET would otherwise accept, and preserves Foundation's rejection of alternate alphabets. Int64
decoding admits decimal/exponent spellings, retains the reference decoder's small-number Double
rounding, and checks Double overflow before recovering a large value from its decimal spelling.
Lexical coefficient/scale conversion retains the reference's truncation toward zero on that path,
without System.Decimal rounding a long fractional suffix into the next integer. Actual observations
include bare Int64.MaxValue (accepted), its `.0` spelling (rejected), `4503599627370495.1` (rounded
and accepted), and `9007199254740993.0` (accepted with its exact integer value above Double's exact
range). Large positive/negative fractional spellings are also captured. These record observed
Codable behavior rather than assuming mathematical JSON-number equivalence. Other graph Int32 fields retain their
current bounds; arbitrary out-of-range raw JSON is not a claim of cross-runtime numeric compatibility.

Fifty probes compile the complete original document/backup model declarations and actual leaf
dependencies. The driver reuses only fixed graph construction from the projection corpus and uses
Foundation's real JSONEncoder/JSONDecoder. Generated input is canonicalized for stable fixture
storage; raw duplicate/UTF/numeric mutations are applied afterward. Expected results are captured
from that executable. The verifier locks all IDs in order and CI recompiles the same source on each
run. Cases cover rich children, optional absent/empty values, base64 over all byte values, future
versions, required/malformed fields, unknown fields, first duplicate keys, UTF-16/BOM/invalid UTF-8
and exact numeric boundaries. Four extra C# facts and four whitespace theory rows protect full-graph
roundtrip, media ownership, wire-versus-restore admission, invalid UTF-16 and fixed failure text.

Comparison is semantic JSON. Mac's default backup encoder leaves object order unspecified, so these
tests do not claim identical serialized bytes, formatting or an exact boundary byte count. Date
precision retains the [row codec's](VOCABULARY_PAYLOAD_CODEC.md) documented .NET/Foundation limits.
The future archive adapter must apply the original 192 MiB input/output and 128 MiB media budgets,
supported backup version, whole-graph validation, exact referenced-image key set and native image
validation before staging/committing. A successful decode alone never authorizes restoration.

```sh
python3 scripts/verify-vocabulary-backup-codec-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyBackupCodec
```

Atomic backup writing, bounded source reads, media snapshot locking, restored SQL revision
acknowledgement/rollback, Store lifetime and explicit UI replacement confirmation remain follow-up
work. This library adds no file, clipboard, model or network effect and creates no downloadable build.
