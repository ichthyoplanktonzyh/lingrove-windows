# Vocabulary graph values, projection and camera

Reference: pinned macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Models/Vocabulary/VocabularyGraphModels.swift`,
`Cotabby/Support/Vocabulary/VocabularyGraphProjection.swift`, and the projection/camera/settings
cases in `CotabbyTests/Support/Vocabulary/VocabularyGraphTests.swift`.

This slice adapts the existing graph's pure values and rules. It supplies no layout solver,
scheduled worker, canvas, graph mode, graph presentation commands or settings codec/persistence.
Those remain subsequent adaptations of the original Mac implementations, including the vendored
`Packages/LingroveGraphLayout` Grape engine. A different force algorithm is not part of this work.

## Ownership and data flow

`VocabularyGraphModels.cs` owns durable-ID-based node keys and immutable node/edge/snapshot/frame
values. A headword key carries its entry UUID; a sense key also carries the exact sense UUID.
Changing a label or position cannot change identity. Snapshot/frame collections detach at
admission, retain source ordering or set/map equality, and contain no archive/native handles,
rich content, images or inference sessions. A future notebook window/worker will exchange these
small copied values rather than share mutable solver buffers.

`VocabularyGraphProjection` receives the already filtered entries, expansion IDs and raw query.
It preserves the original UUID ordering, labels, explicit headword-to-owned-sense edges and query
trimming. A collapsed headword match remains a headword; an expanded entry or non-headword match
reveals its sense branches. Matching senses use the existing full notebook-search policy. That
policy includes the entry's own expression/form, so an accent-insensitive sense probe may match
all children even when the graph's case-only headword comparison did not match. The actual-source
accent cases retain this behavior. Proximity, common labels and generated text create no edges.

`IVocabularyGraphTextSearching` distinguishes Foundation's `localizedCaseInsensitiveContains`
for headwords from `localizedStandardContains` for full sense probes. The existing Windows
adapter now supplies case-only `HeadwordContains`; its old case/diacritic-insensitive `Contains`
behavior is unchanged. Locale/runtime Unicode and collation differences remain a platform
boundary. The existing notebook's documented provider gaps are not silently treated as resolved.

`VocabularyGraphSettings` preserves appearance/force defaults and the six original finite-value
bounds/fallbacks. It is independent of learning data and currently owns no persisted defaults.
`VocabularyGraphCamera` adapts the original copied value: methods return a new camera instead
of mutating a Swift struct. Double-precision point/size values describe canvas/world coordinates,
not focus-screen physical pixels. Screen/world mapping, pointer-anchored zoom and fit do not alter
solver state. Fit preserves the original first zero rectangle and subsequent asymmetric 0.01
rectangles, including the tiny order-dependent offset; it is not replaced by a symmetric box.

## Evidence and limits

The 39th actual-source verifier compiles the complete original graph models/projection with the
existing actual vocabulary models/search/title rules. Its 45 locked cases cover twenty graph
projections, seven settings/default/bound/nonfinite cases and eighteen camera transforms. The
driver only constructs test input and exposes results; exercised method bodies are unchanged.
Source-produced document/query/expansion inputs and actual Foundation substring facts accompany
the projection observations. C# uses those facts to isolate deterministic graph rules rather than
pretending that Windows collation is Foundation. Three additional cases compare the actual
case-only Windows adapter with an explicit simple/accent source subset. They are not a claim of
complete language-provider parity.

The counterpart compares identities, node/edge order, labels, match sets and query exactly as
semantic JSON; camera numbers use 1e-9 absolute / 1e-12 relative tolerance. One extra ownership/
structural-equality case protects detached collections and canonical Unicode label equality.
The 49 targeted Core tests pass locally. This is library evidence, not a graph-rendering or
physics result. Numeric camera cases use finite positions; no corrupt solver output is claimed.
Native layout-buffer ownership, motion/drag/cooling/performance, WPF hit-testing/labels/DPI,
settings decode/persistence, window lifetime and interactive graph behavior remain work.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyGraph
python3 scripts/verify-vocabulary-graph-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
```

No model, clipboard, network, installer, artifact upload or new hosted transmission participates.
