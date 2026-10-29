# Frozen UUID row payload compatibility probe

The shared fixture exercises UUID values in the frozen `collection_interpretations` and
`mapping_evidence` table dispatch. The Windows test runs the actual frozen C# payload codec;
the macOS probe extracts the actual `Table` and `Payload` declarations from
`Lingrove/Models/Vocabulary/VocabularyDatabaseModels.swift` at
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04` and exercises the actual `decode` and `encoded`
methods. Unused non-UUID payload types are inert Codable stand-ins required to compile the
exhaustive source enum; none are selected in the probe. This is a narrow UUID value check, not
verification of any other row shape, SourceDate, full graph or database interchange.

```sh
python3 scripts/verify-vocabulary-sqlite-frozen-uuid-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyUuidInterchangeTests'
```

The Windows test and Python syntax check passed locally. Swift is unavailable on this Windows
host, so native compilation/execution remains pending. No historical verifier pin changed.
