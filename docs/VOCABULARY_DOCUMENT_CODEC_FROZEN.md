# Frozen portable vocabulary document codec

Reference: committed Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. This is a pure
in-memory document boundary, not SQLite, a backup-with-media envelope, or Store activation.

| Frozen Mac evidence | Windows mapping |
| --- | --- |
| `Lingrove/Models/Vocabulary/VocabularyModels.swift`, `VocabularyDocument` Codable | `Core/Vocabulary/Frozen/VocabularyDocumentCodec.cs` decodes flat v7/v8 and encodes flat v8 |
| `VocabularyModels.swift`, `VocabularyLifecycleModels.swift`, `VocabularySemanticModels.swift`, `LearningExplanationModels.swift`, `ReviewModels.swift` | `Core/Vocabulary/Frozen` owns timestamp-bearing v8 values and the native graph validator; unchanged content, review plans, labels, classification and identity primitives remain shared |
| `VocabularyContentModels.swift`, `ReviewLayout.swift`, `ReviewAudioModels.swift` | source-shaped leaf machinery preserves content unions, review enum dictionaries, UUIDs and optional fields; the v3 whole-document codec is not used |
| `VocabularyClassificationTests.swift` version-seven portable upgrade, `VocabularySemanticTests.swift`, `VocabularyClassificationPersistenceTests.swift` | Core tests cover handwritten source-shaped v7, full v8 graph, upgrade and fixed failure cases; native Swift execution remains a separate gate |

`Decode` accepts schema 7 or 8. Version 7 ignores any `classification` field and becomes an empty
directory, as in the Mac decoder. Version 8 requires a directory. Both produce a detached frozen
v8 graph and pass its v8-native admission gate before return. `Encode` validates that graph and emits
`schemaVersion: 8` and source resources at the root. The historical entry-owned v3 and exact-tick
v7 Resources graphs, their codecs and fixtures remain separate.

`Frozen.SourceDate` holds the finite IEEE-754 Double count of seconds from 2001-01-01 UTC used by
Foundation Date. It is the canonical date, not a sidecar or replay value. The document codec reads
and writes that Double directly, including sub-tick and out-of-Windows-range dates. `FromWindows`
explicitly selects the nearest Foundation Double for a `DateTimeOffset`; several Windows ticks can
map to the same Double. `TryToWindows` explicitly returns a nearest tick and whether it was exact,
or fails when the value cannot fit Windows' calendar. Neither operation mutates the source value.
`VocabularyV7Upgrade.Convert` is an explicit detached adapter from the older v7 graph: its typed,
immutable receipts include original UTC ticks, resulting Double, tick-preservation status, owner,
field and source path. Paths distinguish repeated nested explanation/binding IDs. The adapter makes
no durable change; a future migration must decide how to handle any non-preserved Windows ticks.

UUIDs emit uppercase, optional absence survives, languages/enums use source raw values, content
associated cases retain their Swift structure, and enum-key dictionaries use alternating arrays.
Existing Foundation UTF-8/16/32 and first-key handling are reused. JSON object order and escaping
are not byte-identical guarantees. Review endpoints remain strings and no request is sent. Media
references carry metadata only; media bytes, existence and digest verification remain separate.

Input and output are limited to 192 MiB, adapting the frozen archive backup/restore bound in
`VocabularyArchive+Media.swift` to a standalone document. This is an explicit Windows transport
restriction. Transcoded UTF-8 is checked again. Output uses capped chunks and checks the total source
text budget before writing; serializer buffers and the returned array require additional bounded
memory. JSON depth is limited to 512, following existing Foundation decoder observations. Source
lifecycle, semantic, classification, layout and review collection limits are checked before graph
materialization; collections without an upstream count limit are bounded by bytes and admission.
Swift Int fields still modeled as .NET Int32, such as text-range offsets, reject out-of-range values;
classification catalog and framework versions use Int64. These are explicit representability limits.

Tests exercise real-world and near-2001 Double spellings, negative zero and subnormal values,
timestamps outside the Windows calendar, all timestamp-bearing owners, and 100 consecutive ordinary
Windows ticks. They also cover v7 conversion receipts, source-shaped graph roundtrip and wire fields,
unknown language/completeness, reviews/media references, immutable ownership, bad versions/enums,
malformed bytes, nesting and collection limits. The Windows host has no Swift toolchain; these tests
are not a native Foundation Codable run or a byte-parity claim. Database schema/migration, media
transactions, backup envelope, rollback, Store publication and physical UI acceptance remain work.
