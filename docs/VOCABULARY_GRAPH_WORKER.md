# Original graph worker lifetime

Reference: stable Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Vocabulary/VocabularyGraphLayoutWorker.swift`. The Windows counterpart is
`src/Cotabby.Core/Vocabulary/Graph/Layout/VocabularyGraphLayoutWorker.cs`; it owns the already
ported original [Grape simulation](VOCABULARY_GRAPH_LAYOUT.md). This is a per-window layout owner,
not a second notebook Store. Canvas/pointer/rendering and preferences remain follow-up adaptation.

## Ownership and data flow

A notebook presentation retains one worker. `ConfigureAsync` receives immutable graph/settings
and an epoch, `AdvanceAsync` requests one copied frame (with an optional stable pin identity), and
`SuspendAsync` releases simulation particles while keeping window-local coordinates. The async
semaphore is acquired before CPU work is scheduled; all mutable simulation/cache/index/epoch work
is serialized on thread-pool tasks. Calling from WPF cannot synchronously execute the solver on its
UI thread. No caller's synchronization context is needed for the operation to finish.

As in the original actor, operations have no cancellation/dispose API. A canvas cancels its animation
loop and advances its epoch; an already admitted synchronous step finishes, and stale delivery is
rejected by that canvas. An older suspend/configure cannot replace a newer epoch; a step requires
an exact epoch. Awaiting suspend is the boundary for releasing particles. The retained worker's
semaphore never creates a WaitHandle, and the owner eventually releases the whole worker/cache.

Configuration preserves current positions and velocities by stable IDs. A changed topology or
force rebuilds the original solver; label/search/appearance changes update the projection without
reheating a cooled solver. New senses use their cached parent's coordinate plus the original
angular/link-distance offset; initial headword positions use the original golden-angle seed.
Missing edge endpoints are ignored, and a missing pin ID becomes no pin. Configure prunes cached
coordinates only above `max(4096, currentCount*2)`; suspend keeps at most the first 4096 active IDs
when its cache is larger. A final dragged position can be retained before particles are released.

The input contract remains finite coordinates/settings and a valid unique-ID projection, as for
the original actor. Public worker callers do not accept untrusted graph JSON directly. Nonfinite
solver output is replaced by zero only in the published frame, preserving the source's geometry
fallback; it is not silently written back into the physics buffers.

## Evidence and remaining work

`verify-vocabulary-graph-worker-macos-parity.py` compiles the complete unchanged actor, original
graph model file and original selection declaration with the complete unchanged GraphSimulation
and vendored Grape package. `macos_graph_reference.py` shares only package reconstruction/manifest
setup with the particle verifier and checks every force source against its pinned upstream hash.
No worker, force or cache body is rewritten for the test oracle.

The 27 fixture transcripts compare every emitted frame's identity/positions/count/running state
and stale/null responses. Cases cover initial/expanded seeds, reuse/reorder/filtering, pin release,
cosmetic versus force/topology invalidation, old/future epochs, suspend/reopen/final positions,
4096/4097 configure cache thresholds, and suspend cache pruning. The 1000-headword/20-step case
checks full frame count/finiteness and four sampled positions; it is not a comparison of every
particle or a measured performance claim. Small transcripts compare every position. Float tolerance
matches the engine verifier (`1e-8` absolute or `1e-10` relative); keys/counts/running are exact.

Two additional C# cases enqueue configure/ticks/suspend/reopen concurrently to check admission
order/copied frames and verify completion without posting to the caller's UI context. Local
worker target: 29 passed, zero failures/skips. This portable library does not claim WPF interaction,
a visible frame rate, UI automation accessibility, settings restoration or actual window occlusion
behavior; those still require the canvas and its own host validation.
