# Original vocabulary graph particle engine

Reference: stable Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.
This adapts the existing algorithm and parameters. The current round supplies a portable engine;
worker scheduling, rendering, interaction and persisted preferences remain separate work.

| Original source | Windows owner |
| --- | --- |
| `Packages/LingroveGraphLayout/Sources/LingroveGraphLayout/GraphSimulation.swift` | `Core/Vocabulary/Graph/Layout/VocabularyGraphSimulation.cs` |
| `ForceSimulation/Simulation.swift`, `Kinetics.swift` | The same simulation's step/cooling/integration |
| `ForceSimulation/Forces/{ManyBody,Link,Position,Collide}Force.swift`, `SealedForce2D.swift` | The same simulation's ordered, private force methods |
| `ForceSimulation/KDTree/{BufferedKDTree,KDBox,KDTreeNode}.swift` | `GrapeSpatialTree.cs` |
| `ForceSimulation/Utils/{SimulatableVector,LinearCongruentialGenerator}.swift` | `GrapeVector.cs` |

These `ForceSimulation` paths are relative to the pinned package's `Sources/`. They are unchanged
Grape 1.1.0 upstream sources (`48eb7d8`), MIT, Copyright (c) 2023 Zhen Li. The complete license and
original checksum table are retained in `resources/ThirdParty/Grape/` and referenced from
`THIRD_PARTY_LICENSES.md`. The Windows implementation specializes the actual used 2D, unit-mass,
constant-link/target/radius path; unused 3D/custom descriptors and SwiftUI behavior are not copied.

## Ownership and data flow

One future per-window layout worker owns one `VocabularyGraphSimulation` at a time and serializes
all access off the UI thread, as the original actor does. The constructor copies initial particle
positions, velocities and links. Every `Current`/`Step` result copies both arrays into read-only
collections; consumers cannot retain a mutable particle buffer. This engine starts no timer and
reads no notebook/archive/media/network. It is not independently thread-safe.

The five force passes retain their order: many-body, link, x target, y target, collision. The original
many-body pass writes positions immediately while traversing a tree captured before that pass.
The collision pass uses the unjiggled delta for its velocity impulse while using the jiggled delta
for length; these upstream quirks are preserved. The buffered tree retains insertion, quadrant and
cluster order, with managed node references/indices replacing unsafe pointer rebasing. Only the
original nonempty `UnsafeArray` covering-box path is needed; its maxima both add one, so all particles
are already covered and the engine needs no externally callable tree expansion/removal API.

Cooling, seeded UInt32 overflow RNG, degree-weighted link bias, fixed radius 28, fixation handoff
and velocity clearing retain the Mac rules. The same `.65` initial alpha, `.003` minimum, `.035`
alpha decay and `.65` velocity decay are used. Coordinates and parameters supplied by a worker must
be finite, as in the original projection/cache flow. An empty/mismatched direct construction fails
with `ArgumentException`; a negative link or invalid direct pin fails before mutation instead of
accessing an invalid native buffer. Out-of-range positive links are filtered as upstream does.

## Evidence and limits

`verify-vocabulary-graph-layout-macos-parity.py` reconstructs the complete pinned Swift package in
a temporary directory, verifies every vendored ForceSimulation source against its original SHA256
table, adds only a manifest executable driver, and builds Release without a model/network/UI.
No force, tree or adapter body is rewritten. The same 31 input cases execute the real package and
C# engine: every initial and milestone snapshot compares positions, velocities and running state.
Coverage includes individual/moving particles, coincident/near-cluster nodes, quadrant trees,
constant force settings, link degree/duplicates/self/filtered edges, pin handoff, cooling, idle,
and reheat. The largest exact snapshot case has 64 particles. Floating-point comparisons allow
absolute `1e-8` or relative `1e-10` for platform arithmetic; integer topology and running state are exact.

Two additional C# tests cover copied inputs/snapshots, independent instances/RNG streams, and invalid
buffer calls. Local targeted results: 33 passed, 0 failed/skipped. These tests do not claim a
1000-node performance profile, a worker's epoch/cache lifecycle, WPF canvas, real pointer interaction
or visible desktop frame rate. Those retain the original source as their next adaptation boundary.
