# Frozen document Codable compatibility probe

Reference: committed Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. This is a
portable document wire check, not SQLite admission, archive validation or activation. The shared
fixture was generated from the Windows codec's populated graph and is labelled
`windows-generated-source-shaped`; it has not been certified by native Swift. Its three cases
cover a populated version-eight graph, a version-seven document with an ignored classification
field, and absent optional template/content/card collections. The graph carries independent raw
sources and occurrences, accepted claims, collection state, explanation records, semantic nodes,
roles, patterns, mappings, relations, cues and classification relationships. Its review content
includes a text block, image metadata, a layout template, a review binding, enum-key dictionaries,
audio receipt, optional values, Unicode and UUIDs. Framework version `2147483648` exercises the
Swift Int64 boundary. No image bytes or user notebook are used.

The Windows consumer decodes each input through the real frozen `VocabularyDocumentCodec`,
compares its emitted JSON semantically with the fixture expectation, and checks exact binary64
bits for 15 fields covering each timestamp-bearing owner: entry, sense, source, interpretation,
explanation, collection, review binding and semantic node. It repeats the bit checks after
re-encoding. The native driver runs the real frozen Swift `VocabularyDocument` and leaf Codable
declarations using Foundation `JSONDecoder` and `JSONEncoder`, compares JSON values without
requiring object-key order or escaping equality, and checks the same typed date fields before and
after an encode/decode cycle. Any native mismatch is a finding to investigate, not permission to
rewrite the shared expectation to match one side.

The verifier reads these exact full files with `git show` at the frozen revision:

- `Lingrove/Models/Context/LanguageCatalog.swift`
- `Lingrove/Models/LanguageLearning/LearningLanguage.swift`
- `Lingrove/Models/Vocabulary/VocabularyModels.swift`
- `Lingrove/Models/Vocabulary/VocabularyLifecycleModels.swift`
- `Lingrove/Models/Vocabulary/VocabularySemanticModels.swift`
- `Lingrove/Models/Vocabulary/VocabularyClassificationModels.swift`
- `Lingrove/Models/Vocabulary/VocabularyContentModels.swift`
- `Lingrove/Models/Review/ReviewModels.swift`
- `Lingrove/Models/Review/ReviewLayout.swift`
- `Lingrove/Support/Vocabulary/VocabularyNotebookProjection.swift`

It also extracts unchanged source declarations from `Lingrove/Models/LanguageLearning/
LearningExplanationModels.swift` (Codable values, omitting the UI protocol),
`Lingrove/Models/Review/ReviewAudioModels.swift` (audio Codable values, omitting preparation
state), and `Lingrove/Support/Vocabulary/VocabularyPolicy.swift` (the original error enum). No
model or Codable stand-ins are used. The complete notebook projection is compiled because the
original document declares joined-entry accessors; this probe does not exercise those accessors.

Run on macOS with Swift and the frozen Git commit present:

```sh
python3 scripts/verify-vocabulary-document-frozen-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

Run the Windows consumer:

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyDocumentInterchangeTests'
```

The Windows consumer passed locally, and Python syntax plus all source extraction boundaries and
fixture timestamp paths were checked on Windows. Swift is unavailable on this host; compilation
and native observations remain pending CI. Even a successful native run establishes only these
Codable cases, not full semantic admission, populated SQLite files, schema migration, media,
backup, Store integration or production activation. Earlier historical verifier pins remain unchanged.
