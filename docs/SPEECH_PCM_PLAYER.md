# PCM playback facade

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/SpeechPCMPlayer.swift`, renderer protocol from `SpeechPCMRenderer.swift`,
provider events/phases from `Models/Speech/LearningSpeechModels.swift`, and the six regressions in
`CotabbyTests/Services/Speech/SpeechPCMPlayerTests.swift`. This adapts the existing facade; it does
not implement an audio device, generator, speech controller, AppHost registration or a playback UI.

`Core/Speech/SpeechPlaybackContracts.cs` keeps immutable token-bearing provider events, the two
playback phases and the original facade/renderer seams independent of WPF and native devices.
`App/Speech/SpeechPCMPlayer.cs` is one controller-owned queue/event facade. It borrows one renderer,
receives validated immutable PCM, and emits prepared/started/finished/failed events for the original
token. Stop itself emits no cancelled event. The renderer owns native serialization and must return
promptly; the facade neither creates a device nor moves a blocking native implementation onto UI.

Begin retires the preceding utterance, increments the revision again, clamps rate to0.5–1.5 and
starts preparation while generation can start independently. The first block awaits the same
preparation barrier and rechecks cancellation and token afterwards. A sample-rate mismatch fails
before readiness/scheduling. At least half an audible second starts playback, adjusted by rate;
EOF starts a shorter final tail. Two audible seconds apply20ms cancellable backpressure. The
[existing ledger](SPEECH_PCM_FOUNDATION.md) supplies the lifetime budget, monotonic buffer IDs and
idempotent acknowledgements. Queue drain alone does not finish generation. EOF plus zero queued
samples and a drained renderer output tail finishes exactly once; EOF also starts the original queued-duration-plus-five-second
watchdog. Preparation/device/watchdog failure retires the current token before emitting invalidAudio.

Stop clears token/queue/preparation/watchdog/sample state before requesting native retirement.
Old buffer callbacks and device notifications cannot retire a new token, including when buffer ID0
is reused. The renderer contract retains revisions because queued stop/prepare calls need not arrive
in order: prepare requires a strictly newer revision, enqueue/play the current active one, stop an
equal/newer revision. Stop pauses/reuses resources; shutdown releases them. The future Windows
renderer must invalidate its graph before acknowledging discarded buffers, detach device callbacks,
and avoid stopping native audio inside its render-completion callback.

The [Windows tempo adaptation](SPEECH_PCM_TEMPO.md) adds explicit EOF flush and output-drain barriers
for a processor whose analysis window lives outside the native output queue. Existing renderers use
completed defaults. Flush precedes ledger EOF even when the queue is already empty; a tracked output
barrier prevents that empty ledger from stopping a newly flushed tail. The watchdog still applies,
and shutdown joins the real barrier after retirement rather than abandoning it on cancellation.

Mac MainActor becomes an explicit caller-owned serial UI SynchronizationContext. Construction/public
calls require that owner; native callbacks marshal back to it, while async state resumptions keep it.
No plain thread-pool context is a valid substitute. Task admission reserves the handle before a
synchronously completing renderer can execute. Facade-owned preparation/watchdog/stop work remains
tracked even when native preparation ignores cancellation. The host must stop/join its generator and
enqueue calls, call Shutdown, then await DrainAsync before releasing renderer/UI context. This extra
Windows lifetime adapter observes task errors without dropping the readiness error seen by Enqueue.
.NET cancellation callbacks can throw, unlike Swift Task.cancel: retirement collects that exception,
finishes cleanup, and reports it at Drain. A renderer violating its nonthrowing Stop contract is also
reported after accepted work joins. These are explicit adaptation errors, not user audio messages.

## Evidence

`fixtures/parity/speech-player-v1.json` has33 full traces: six original-test adaptations and27
source-policy extensions. `verify-speech-player-macos-parity.py` compiles the complete unchanged
original facade, chunk/ledger/models and renderer protocol, plus the verbatim original GatedPCMRenderer.
Read-only extensions observe private state; its unused native default renderer is a fatal stub,
so every exercised call must use the original test seam. No original Swift Testing suite is claimed.
Each action compares token/revision/rate/readiness/playback/queue/EOF/watchdog state, renderer
preparation/enqueue revisions, ordered events and exact invalidAudio/cancelled outcome.

Coverage includes held readiness and cancellation, late old preparation failure, colliding buffer
IDs, device invalidation/recovery, wrong-format admission, exact half-second and rate-adjusted
thresholds, short EOF, generation gaps, duplicate/unknown callbacks, empty/repeated EOF, enqueue after
EOF, caller cancellation, two-second backpressure/drain/stop and a real watchdog expiry. Only the
uninjectable preparation elapsed milliseconds become a measured marker; event position remains
exact. Tokens use driver-assigned names, preserving identity without random UUIDs. No event/error or
queue value is discarded. Reviewed unique case IDs/hash are locked in both runners.

The initial cross-language run found one observation race: a shared readiness task can wake its
producer and the observer in either order. Both drivers now join waiters associated with the released
preparation before recording that action. Actual Mac results were recaptured; only that one
intermediate trace changed. Expectations are generated from the source executable, not from C#.
Other asynchronous actions use explicit preparation, task-entry and callback-completion barriers.
All accepted tasks/gates are released and joined during test cleanup before its serial context closes.

The actual C# facade consumes the same33 traces. Four portable facts cover worker-to-owner callback
dispatch/held-preparation drain, retirement revisions/native contract failure, required context and
off-thread admission, and throwing-cancellation cleanup. A two-row theory covers suspended enqueue
and play stale-result guards: six additional cases,39 new portable cases total. The compiled WPF suite adds one worker-callback/held-preparation case
on its real Windows STA dispatcher. It uses a gate, not an audio device. The63rd Mac CI source verifier
includes the exact additional sparse paths. Native source verification passed all33 traces, including
an independent read-only run. All39 new portable cases pass in the full App1025 suite; Core4420
also passes, with zero failures/skips. The Release solution cross-build passed with zero warnings
and errors. Both final-head CI jobs remain required before merge.

## Remaining integration

Actual Windows PCM rendering, pitch-preserving speed, encoded playback, installed system voices,
system/local/endpoint generation, helper/runtime lifetime, shared controller, UI/settings and
review-audio composition remain work. Device loss/replacement, audible underruns/tails, shutdown,
latency and physical Windows interaction need native evidence. These gated tests and a successful
cross-build do not establish audible parity. No package, model download, audio artifact or new
transmission is introduced.
