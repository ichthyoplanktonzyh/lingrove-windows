# Offline lexical recognition rules

This slice adapts stable Mac Lingrove at `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.
It supplies the explicit lexical index, occurrence values and Windows text primitives needed by a
later dictionary/card integration. The [subsequent resource/store slice](LEARNING_OFFLINE_LEXICON.md)
adds the pinned lexicon and shared lazy service. [Completed-translation double-click lookup](LEARNING_WORD_PREVIEW.md)
now uses that index; source-word fallback and system/MDX/online dictionaries remain unported.
Translation request, normalization and provider contracts remain unchanged.

## Source and responsibility mapping

| Pinned Mac source / tests | Windows adaptation |
| --- | --- |
| `Cotabby/Models/LanguageLearning/LearningUnitModels.swift` | `Core/LanguageLearning/LearningUnitModels.cs`: word/expression/pattern targets, explicit dictionary entries and source-owned senses/attribution |
| `Cotabby/Models/LanguageLearning/LearningDictionaryWord.swift` | One original-text occurrence and exact UTF-16 range; AppKit text anchors are outside this slice |
| `Cotabby/Support/LanguageLearning/LearningUnitRecognizer.swift`, `CotabbyTests/Support/LanguageLearning/LearningUnitRecognizerTests.swift` | `LearningUnitRecognizer.cs`: alias index, explicit forms, longest expression scan, known slots, separate slot click targets and stable occurrence ordering |
| `Cotabby/Support/LanguageLearning/LearningUnitPolicy.swift` | `LearningUnitPolicy.cs`: grapheme whitespace/newline handling and exact hit testing; `ILearningLexicalText.cs`: platform casing/word/character contracts |
| Swift Character properties and Mac `NLTokenizer(unit: .word)` used by that policy | `Win32/Text/IcuLearningLexicalText.cs`: full root-locale scalar Unicode casing, binary Alphabetic/Uppercase properties and ICU word breaking |

The Mac lexicon service owns lazy resource loading and per-language indices. Its Windows service and
pinned resource are now adapted separately; the bundled card/session subset is also connected,
while broader provider routing remains later work.
Windows paths above are relative to `src/`; tests mirror those responsibilities under `tests/`.

## Preserved index behavior

An index receives immutable explicit entries for one language and an injected platform text adapter.
It never queries the editor, infers suffix forms, generates meanings or makes network requests.
Dictionary entry IDs and source attribution stay distinct from vocabulary UUIDs or generated output.
Inputs and output collections are detached from caller-owned lists.
Snapshot equality compares ordered collection contents and canonically equivalent strings, matching
Swift values; comparison keys never rewrite stored text or its ranges.

Keys use NFC, full Unicode lowercase, curly-apostrophe replacement and grapheme whitespace collapse.
Only keys are normalized: displayed surfaces, queries derived from a literal expression, word
occurrences and UTF-16 ranges retain the original text. Repeated occurrences remain separate.
An explicit headword/form is required. An isolated verb alias cannot stand for a multiword headword;
Chinese/Japanese/Korean aliases shorter than two graphemes are excluded, as on Mac.

The scan prefers the longest explicit expression within twelve words before trying a pattern.
Only explicit `one's`, `someone's`, `someone`, `somebody` and `something` placeholders license slots,
with the same possessive/pronoun/name and contraction checks. At least two fixed tokens are needed.
Patterns preserve separated fixed ranges and expose slot words independently. Punctuation and
newlines cannot become invented grammatical links. The pattern length itself is not shortened to
the explicit-expression scan limit. C# also validates provider word ranges, exact original text,
non-overlap and grapheme edges before constructing click targets.

## Platform boundary

`ILearningLexicalText` keeps the platform-owned Unicode and segmentation behavior visible. The
Windows implementation uses only the system ICU DLL, C calling convention, and per-call pinned
UTF-16 buffers. Word iterators close in `finally` before the buffer unpins. Case conversion preflights
required output size and validates it before allocation. Loader/API failures produce a fixed
unavailable category; there is no punctuation or suffix fallback.

Full per-scalar case mapping preserves expansions without contextual final-sigma changes, matching
Swift 6.2 String.lowercased/uppercased. The initial Windows CI caught whole-word ICU casing
changing a Mac key; the adapter now follows the actual scalar loop. Alphabetic and Uppercase use
Unicode binary properties rather than only general categories. This follows the relevant
[ICU casing API](https://unicode-org.github.io/icu-docs/apidoc/released/icu4c/ustring_8h.html),
[ICU property contract](https://unicode-org.github.io/icu-docs/apidoc/released/icu4c/uchar_8h.html),
[Swift String casing implementation](https://github.com/swiftlang/swift/blob/swift-6.2-RELEASE/stdlib/public/core/String.swift), and
[Swift Character implementation](https://github.com/swiftlang/swift/blob/swift-6.2-RELEASE/stdlib/public/core/CharacterProperties.swift).
ICU replaces Apple's word tokenizer as an OS dependency; neither common-case fixtures nor a green
compile establish universal segmentation equivalence across languages, OS versions or new Unicode data.
Grapheme enumeration/counting, whitespace classification and range-edge validation currently use
the .NET runtime's `StringInfo`/Unicode tables in both Core and the adapter. These are additional
version-sensitive platform primitives, not recorded-oracle operations; only the committed cases
constrain their equivalence to Swift. An unfamiliar boundary is rejected before becoming a click
target. Universal grapheme parity remains unverified.

## Evidence and limits

`learning-lexical-v1.json` contains 34 cases captured from the actual pinned Mac implementation.
The initial 33 were captured before writing the C# recognizer; one source-derived casing regression
was added after Windows CI exposed contextual Sigma behavior. All existing expectations stayed
unchanged. Thirteen are regression adaptations and twenty-one are source-policy extensions;
extensions are not claimed to be existing Mac tests. Additional queries/hit probes in
an adapted row also come from the real policy, not assertions attributed to the old test.

The Swift runner compiles complete committed `LearningUnitPolicy.swift` and
`LearningUnitRecognizer.swift`, actual language/catalog sources, and the real pure word/unit/entry
declarations extracted from UI-dependent files. It invokes the actual `NLTokenizer`, lookup,
recognition, key and hit-test rules. It also records 624 text facts: domain keys plus Swift character
predicate projections needed to isolate the OS boundary. No policy method is replaced by a stub.
Its explicit capture option writes observations to a new file without overwriting existing fixtures.
Normal CI compares the committed expected values and never updates them.

Portable C# tests feed those recorded words/facts to the production recognizer; this test-only oracle
is not an App provider. Windows tests separately execute actual system ICU against every recorded
text fact, plus the 31 predeclared English/Spanish/French word-segmentation/recognition cases. The three remaining
mixed-script/CJK/casing-context cases still exercise native Unicode facts and exact range reconstruction, while
keeping their Mac segmentation as a reference rather than a universal Windows promise.

Tests also cover invalid native ranges, malformed UTF-16, unavailable APIs and detached source
collections. These lexical policy cases do not exercise a Windows editor, WPF word hit geometry, lexicon-file loading,
dictionary content quality, lookup integration or model behavior. No resource/model download,
installer, artifact upload or release is part of this slice.
