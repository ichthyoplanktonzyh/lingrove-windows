# Offline lexicon resource and service

This slice adapts Mac Lingrove at `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.
It supplies the actual offline resource, bounded decoder/file source and one lazy shared store.
The App composition root registers that store; the sentence preview does not yet call it or show
clickable words. System dictionaries, MDX/MDD, websites and dictionary cards remain unported.

## Source and ownership

| Pinned Mac source | Windows adaptation |
| --- | --- |
| `Cotabby/Models/LanguageLearning/LearningUnitModels.swift` | `Core/LanguageLearning/LearningLexiconResource.cs`: immutable resource metadata/entries and the bundled entries/not-found result subset |
| `Cotabby/Services/LanguageLearning/LearningLexiconService.swift` | `LearningLexiconService.cs`: serialized lazy loading, per-language index cache, recognition and bundled lookup |
| `JSONDecoder` resource decoding in that service | `LearningLexiconResourceDecoder.cs`: required and optional JSON fields, nested senses and original text; no IO |
| `Bundle.main.url(forResource:)`, `Data(contentsOf:)` | `Win32/LanguageLearning/BundledLearningLexiconResourceSource.cs`: bundle resolution and bounded file reads; `App/Hosting/AppHost.cs`: one lazy source/store registration |
| `Cotabby/Resources/learning-expressions*` | `resources/LearningLexicon/`: unchanged pinned JSON, NOTICE and provenance, copied with the app's ordinary compile output |
| `CotabbyTests/Services/LanguageLearning/LearningLexiconServiceTests.swift` | Portable store/resource tests and Windows actual-resource/ICU tests under `tests/` |

Source/test paths in the right column are relative to `src/` unless stated otherwise.
The Core store depends on a narrow resource source and lexical text adapter. A semaphore plus a
worker task replaces the Mac actor's serialized off-UI work. The Win32 source owns file effects;
the decoder and recognition rules stay portable. WPF does not create another store or own indices.

The first request loads the resource; a successful immutable snapshot stays for the app lifetime.
An index is built once per resource language code. Simplified and Traditional Chinese share `zh`
entries, while tokenization still receives the original requested script language. Failed loads are
not cached. There is no reload, automatic fetch, HTTP client or model dependency.

Lookup preserves the nonempty, 256 UTF-8 byte and insertion-safety gates. Expression lookup permits
recorded explicit forms; ordinary word lookup also requires the normalized headword to match.
An unknown query returns not-found. Missing bundle assets resolve to the Mac unavailable state;
an explicitly resolved but unreadable/corrupt/oversized file gives the fixed read-failure state.
The Mac service catches its own size error and reports that same read-failure message; Windows
preserves the observable result while rejecting over-64,000,000-byte input before allocation.
The reader also detects growth past that limit. Parser/path/input details are not exposed.

Cancellation follows the actual Mac boundaries: recognition checks before and after synchronous
work; bundled lookup checks on entry. Cancellation during an accepted bundled read can therefore
still produce a lookup result. Future card owners must reject retired identities before display;
this store's result never grants authority to edit a host. Queued canceled work does not load data.

## Resource and attribution

The resource is version `kaikki-2026-10-03-v1`, 11,543,088 bytes, with 34,397 entries across 13 resource
codes. Its SHA-256 is `ff66f9835d9cf8f586d0618c09ad3e2b091269bc15bcdac1502660dd0e4c7984`.
Each entry retains its stable source ID, headword, explicit forms, nested senses, labels and source
URL. Optional phonetics/examples/source-license fields remain optional; this dataset does not
invent them. Dictionary IDs remain separate from future user vocabulary/sense/usage UUIDs.

The copied [NOTICE](../resources/LearningLexicon/learning-expressions.NOTICE.md) identifies the
Wiktionary/Wiktextract/Kaikki attribution and CC BY-SA 4.0 data license, independently of application
source licensing. Definitions are in English and coverage differs by language. The unchanged
[provenance](../resources/LearningLexicon/learning-expressions.provenance.json) records 95 source
files, dates, checksums, transformations and counts. No upstream data is fetched or refreshed in
this slice. Byte-preserving Git attributes prevent checkout newline conversion.

## Evidence and limits

The existing lexical Swift verifier now compares all three copied files with the pinned Git bytes.
It extracts the complete actual Mac Codable resource declaration and round-trips all entries through
it, comparing the whole JSON value. No service implementation or decoder policy is stubbed.
Its existing 34 lexical cases remain unchanged.

Portable tests execute the actual managed file-source code, decoder and store. They check the
resource hash/size/counts/attribution, malformed required fields and Unicode, optional fields,
missing/corrupt/oversized files, retry, headword-versus-form rules, byte budgets, concurrent loading,
off-UI work and the Mac cancellation boundaries. Blocking/gate tests have bounded waits. The reader's
growth-after-Length check is present but has no deterministic concurrent-file mutation test.

Windows tests combine the actual resource with system ICU for the English and six multilingual
examples from the pinned Mac service tests. These cases do not establish general NLTokenizer/ICU
equivalence; the [lexical platform/version limits](LEARNING_LEXICAL_RULES.md) still apply. The managed
JSON decoder's default depth limit is also a platform constraint; the pinned resource is within it.
There is no editor, WPF hit-geometry, native dictionary UI or resource-update compatibility claim.
Routine CI creates no installer, downloadable build, artifact upload, release or model download.
