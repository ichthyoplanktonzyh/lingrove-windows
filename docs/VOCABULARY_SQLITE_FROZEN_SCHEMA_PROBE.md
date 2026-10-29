# Frozen SQLite schema compatibility probe

The shared manifest is generated from the Windows schema adapter at commit `4e13a25`, not from
native Swift. The Windows test compares fresh schema 5 and the schema left after removing the
five classification tables with that manifest. The independent native probe extracts the actual
schema creation, table enum, SQLite connection and classification migration declarations from Mac
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, constructs an isolated in-memory database, and
compares schema 5 and schema 4 with that same manifest. It then runs the actual Swift 4-to-5
migration and checks its resulting schema and version. It omits the two app-specific logging calls
and supplies the connection's error enum; no schema or migration body is substituted.

Run on macOS with Swift and SQLite available:

```sh
python3 scripts/verify-vocabulary-sqlite-frozen-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

The Git checkout must contain the frozen commit and these paths: `Lingrove/Models/Vocabulary/
VocabularyDatabaseModels.swift` and `Lingrove/Services/Vocabulary/VocabularySQLiteConnection.swift`,
`VocabularySQLiteDatabase.swift`, `VocabularySQLiteMeaningSchema.swift`, and
`VocabularySQLiteClassificationSchema.swift`. The Windows consumer runs with:

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySchemaCompatibilityTests'
```

The Windows consumer passed locally; Python syntax was checked locally. Swift is unavailable on
this host, so the macOS probe has not been executed. This schema-only probe does not establish
row-payload interchange, populated database admission, graph fidelity, or production activation.
Those remain separate gates. No historical verifier pin is changed.
