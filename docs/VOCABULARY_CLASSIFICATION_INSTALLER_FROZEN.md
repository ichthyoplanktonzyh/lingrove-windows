# Frozen bundled classification installer

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, including
`Lingrove/App/Core/LingroveAppEnvironment.swift`, `Lingrove/Services/Vocabulary/VocabularyArchive.swift`,
`Lingrove/Support/Vocabulary/VocabularyClassificationCatalog.swift`, `VocabularyClassificationPolicy.swift`,
`Lingrove/Models/Vocabulary/VocabularyClassificationModels.swift`, and
`LingroveTests/Support/Vocabulary/VocabularyClassificationTests.swift` /
`LingroveTests/Services/Vocabulary/VocabularyClassificationPersistenceTests.swift`.
Mac enables installation for the production archive and disables it for its isolated test host.
Loading retries a failed catalog load even if the document is already cached; successful installation
commits one revision, and an equal/newer framework receipt makes later loads a no-op.

`Frozen/VocabularyClassificationInstaller.cs` stages a candidate against the canonical document's
actual meaning-node IDs using the existing shared `Resources/VocabularyClassificationCatalog`
rules and classification values. It preserves the entire unrelated document graph, identities,
memberships, user categories and parent edges, hidden/local overlays and retired definitions; no
meaning or membership is invented. Invalid candidates throw before publication. `InstallBundled`
and the injectable loader overload skip all resource access while disabled and do not cache failed
loads. The owner serializes fresh read, candidate validation and conditional commit under its
existing archive transaction; this pure library performs no file, database or application activation.

The tracked Windows catalog and the frozen Mac `Lingrove/Resources/VocabularyClassificationCatalog.json`
have the same Git blob `ee9641a88b970b83e5822b3bf001c2a6b9aa8cb8` (58,313 Git-blob bytes).
The existing embedded resource registration remains the authority; no older resource boundary is
silently repinned. The scoped Git attribute enforces LF checkout, and the test hashes the raw embedded
bytes using Git's blob envelope. Existing worktrees must refresh this file after adopting the attribute.

Tests use detached documents and synthetic catalogs, covering disabled loading, failed-load retry,
exact frozen bytes, all 256 bundled categories, idempotent/higher receipts, preserved graph IDs and
source-date bits, retirement, local overlays, custom child edges, memberships and atomic rejection.
SQLite commit/reopen/retry and production Store composition remain the archive owner's integration
work; no existing user notebook was accessed. Native Swift execution and physical notebook UI remain
separate validation gates.

Windows x64 SDK 10.0.402: `dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj
--configuration Release --filter 'FullyQualifiedName~FrozenClassificationInstallerTests|FullyQualifiedName~VocabularyFrozenClassificationTests'`
passed 30/30, including the raw embedded blob hash. `dotnet build Cotabby.sln --configuration Release`
passed with zero warnings/errors. These checks do not execute the macOS application or open a user notebook.
