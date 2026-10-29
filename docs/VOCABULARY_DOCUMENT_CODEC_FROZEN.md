# Frozen portable vocabulary document codec

Reference: committed Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. This is a pure
in-memory document boundary, not SQLite, a backup-with-media envelope, or a Store activation.

| Mac evidence | Windows mapping |
| --- | --- |
| `Lingrove/Models/Vocabulary/VocabularyModels.swift`, `VocabularyDocument` Codable | `Core/Vocabulary/Resources/VocabularyFrozenDocumentCodec.cs` decodes the flat v7/v8 document and encodes flat v8 |
| `VocabularyLifecycleModels.swift`, `VocabularySemanticModels.swift`, `VocabularyClassificationModels.swift` | existing detached resource values, with exact `sourceID`, `interpretationIDs`, `evidenceIDs` and other source field spellings |
| `VocabularyContentModels.swift`, `Models/Review/ReviewModels.swift`, `ReviewLayout.swift`, `ReviewAudioModels.swift`, `Models/LanguageLearning/LearningExplanationModels.swift` | narrow resource-specific metadata extends the existing source-shaped leaf machinery; no v3 document or SQL projection is used |
| `VocabularyClassificationTests.swift` version-seven portable upgrade, `VocabularySemanticTests.swift`, `VocabularyClassificationPersistenceTests.swift` | Core tests cover handwritten source-shaped v7, complete v8 graph, upgrade, strict failures and owned snapshots; Swift execution is a separate gate |

`Decode` accepts schema 7 or 8. Version 7 has no directory; its `classification` field, if present,
is ignored just as in the Mac decoder, and the internal v8 envelope receives an empty directory.
Version 8 requires the directory. Both paths retain lexical, source, occurrence, interpretation,
collection, semantic, review/template and media-reference values; they invoke the frozen graph
gate before returning. `Encode` validates first and emits `schemaVersion: 8` with those fields at
the root. The internal v7 resource marker is not emitted as the document schema. No projection
through the historical entry-owned v3 graph occurs. Encode/decode failures use fixed domain errors
without text or paths.

The JSON boundary retains UUID uppercase output, optional absence, language raw codes, exact enum
spellings, the Swift content associated-value structure and enum-key dictionaries as alternating
arrays. It shares the existing Foundation UTF-8/16/32 recognition and first-key handling. It does
not promise byte-identical object ordering or JSON escaping. Review endpoints remain retained
strings and validation does not connect to them. Image references carry metadata only; media bytes,
existence and digest verification are separate.

Input and output are limited to 192 MiB, adapting the frozen archive backup/restore bound from
`VocabularyArchive+Media.swift` to a standalone document; this is an explicit Windows transport
restriction. UTF transcoding is checked again against that limit. Output is written to capped
chunks; text size is checked before the writer starts, although serializer buffers and the returned
byte array require additional bounded memory. The JSON nesting bound is 512, following the existing
Foundation decoder observations. Source lifecycle, semantic, classification, layout and review
collection limits are checked on parsed JSON before graph materialization; collections without an
upstream count limit are bounded by transport bytes and whole-graph validation. This does not imply
an on-disk schema or database transaction.

Swift `Date` is a Double count of seconds from 2001 and spans a wider range than .NET
`DateTimeOffset` (years 1–9999, 100 ns ticks). This codec rejects out-of-range or fractional wire
dates that cannot be represented as integral ticks; it also rejects tick values that cannot survive
the Swift Double-to-tick roundtrip. It does not round or clamp them. Other Swift `Int` fields that
map to .NET Int32, such as text-range offsets and classification framework version, reject values
outside Int32; no truncation is performed. These are interoperability limits and can reject an
otherwise valid Mac document. The prior v3 leaf codec retains its own historical date behavior.

Tests cover valid graph roundtrip and source leaf shapes, manual v7 upgrade including ignored extra
classification, noncollection evidence, unknown language/completeness, encoded review maps/content,
UTF-16 input and detached ownership, required/null/enum/version errors, dangling references,
malformed bytes, nesting and collection limits, exact tick and range failures. They do not run
Foundation's frozen Codable implementation; source-shaped cases and Windows tests are not proof of
cross-runtime byte parity. Database schemas/migration, media transactions, backup envelope, rollback,
Store publication, app UI and physical acceptance remain future work.
