# Frozen relational projection and populated SQLite probes

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. The shared row manifest is
`windows-projection-source-shaped`, generated from the Windows frozen projection of the rich
document corpus in `vocabulary-document-frozen-v1.json`. It is not a captured native fixture.
A native mismatch is evidence to investigate against the committed source, not permission to
change an expectation merely to match one runtime.

The full v8 case contains 33 rows across all 29 tables, including independent raw source evidence,
occurrences and claims, collection links, sense/usage explanations, content/review/binding/template
headers, meaning and classification relationships. The v7 case upgrades to v8 with an empty
classification directory, and the third case retains absent optional content/cards/templates.
Both runtimes compare each table, row ID, nullable parent, position, nullable extra indexes and
semantic JSON payload against the same manifest. JSON object order and escaping are intentionally
ignored; payload arrays, UUIDs, absent fields and relationships are not. Fifteen typed date fields
cover every timestamp owner and are compared as IEEE-754 bit patterns after database reopen.

The Windows consumer uses the real frozen document codec, projection and named-file SQLite
adapter in a private temporary directory. It writes the graph, closes and reopens it, checks the
revision and all stored row values, and verifies exact reconstructed timestamps. For the v7 case it
removes empty classification tables to form an isolated populated schema-4 database, reopens it
through the real 4-to-5 migration, and checks graph, rows, revision and timestamps. It does not
open an existing notebook or convert schema 2.

The native harness compiles actual frozen Swift document model sources from
`verify-vocabulary-document-frozen-macos-parity.py`, then the complete following files:

- `Lingrove/Models/Vocabulary/VocabularyDatabaseModels.swift`
- `Lingrove/Models/Vocabulary/VocabularyCLIReceipt.swift`
- `Lingrove/Support/Vocabulary/VocabularyDatabaseProjection.swift`
- `Lingrove/Support/Vocabulary/VocabularySemanticDatabaseProjection.swift`
- `Lingrove/Support/Vocabulary/VocabularyClassificationRows.swift`
- `Lingrove/Support/Vocabulary/VocabularyLifecyclePolicy.swift`
- `Lingrove/Support/Vocabulary/VocabularySemanticPolicy.swift`
- `Lingrove/Support/Vocabulary/VocabularyClassificationPolicy.swift`
- `Lingrove/Support/Vocabulary/VocabularyMeaningLabelPolicy.swift`
- `Lingrove/Support/Suggestion/Acceptance/InsertionSafetyGate.swift`
- `Lingrove/Services/Vocabulary/VocabularySQLiteFileLock.swift`
- `Lingrove/Services/Vocabulary/VocabularySQLiteDatabase.swift`
- `Lingrove/Services/Vocabulary/VocabularySQLiteMeaningSchema.swift`
- `Lingrove/Services/Vocabulary/VocabularySQLiteClassificationSchema.swift`

It also uses the complete `VocabularySQLiteConnection.swift` with its app-specific logging import
and two logging calls removed, and exact unchanged exercised method/constant slices from
`Lingrove/Support/Vocabulary/VocabularyPolicy.swift`,
`Lingrove/Support/LanguageLearning/LearningExplanationPolicy.swift`, and
`Lingrove/Support/Prompting/ModelContextBudget.swift`. The reused document probe documents its own
model slices. No substitute model, projection, validation, SQLite, or payload implementations are
used. The native program projects and reconstructs each document, writes and reopens an actual named
SQLite file, verifies its revision, stale-write rejection, row payloads and timestamp bits, and
runs the populated v7-derived schema-4 upgrade. It runs only in an isolated temporary directory.

```sh
python3 scripts/verify-vocabulary-relational-frozen-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySQLiteTests|FullyQualifiedName~FrozenVocabularyRelationalInterchangeTests'
```

The Windows focused suite passed 14/14. Python syntax, all 31 committed source input/extraction
boundaries and the fixture's 29-table and date-path coverage were checked locally. Swift is not
installed on this host; native compilation and observations remain pending macOS CI. Logical rows
and named-file behavior are compared against a shared manifest in separate runtimes; neither side
opens a binary SQLite file produced by the other. Full cross-OS file admission, complete malformed
input coverage, media, backup and Store/application activation remain open. No earlier verifier pin
or user data is changed.
