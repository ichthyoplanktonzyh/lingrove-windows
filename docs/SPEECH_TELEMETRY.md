# Speech observation and bounded request history

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/SpeechTelemetry.swift`, `Models/Speech/SpeechRuntimeModels.swift`,
`Models/Runtime/Metrics/ModelRequestMetrics.swift` and `PerformanceMetricsStore.swift`.
This adapts the original observer/history needed by the shared speech controller. It activates no
AppHost observer, performance pane, player, helper process, model or network request.

`App/Speech/SpeechTelemetry.cs` owns active measurements, the 16-token recent correlation window
and the published runtime snapshot on one caller-owned UI context. `Core/Speech/SpeechRuntimeModels`
keeps detached session/generation/PID/phase and exact loaded asset names. Frozen canonical sets
protect retained snapshots from later generator/settings mutations; equality includes every field.
PID/assets are supplied by the runtime owner and remain through stopping until a reaped-process
event clears them. The observer never guesses that an exit occurred or changes settings paths.

Begin creates two independent short correlation IDs from the original UUID/Crockford algorithm
and starts a fresh measurement for that token. Generated audio records first-ready time once while
accumulating byte counts. Playback start and generation completion each record their first stage;
prepared events remain separate lifecycle observations. Finish removes an active measurement once,
retains its context in the recent window, emits its outcome and optionally appends history. Reusing
a token follows the source's dictionary/queue behavior, including duplicate recent queue entries.
Durations use the injected monotonic clock, Swift's nearest-away-from-zero rounding and a zero
floor. Wall time is used only for the history timestamp. Speech latency measures the wait until
playback, or elapsed wait at finish if playback never began; it does not chart the spoken duration.

Receive resolves context and records a load duration **before** admitting runtime publication.
An old generation/session/sequence can still patch its own existing history and emit its correlated
lifecycle event while leaving the newer PID/assets unchanged. Runtime generation cannot decrease;
a different session is admitted only in loading. A changed session resets sequence tracking.
Sequence zero is always accepted and sets the counter back to zero; subsequent nonzero sequences
must exceed that current counter. Tokenless/unknown-token events may inherit the retained runtime
context only for the same session. Recent eviction removes both context and metric-entry mapping;
an evicted token can still use that same-session fallback for logging, without regaining its
history patch capability.

Lifecycle logging is independent of performance opt-in. Tracking is read at finish and again for
late history patches; changing it does not erase old rows. `LearningRequestContext` carries only
request/operation IDs, task, model name and engine. Event dictionaries contain the original stage,
counts, timing, PID and fixed reason/error-type metadata. They are detached read-only projections.
A real structured speech-log sink is an explicit platform dependency rather than a silent default;
the future AppHost/controller composition must supply its existing logger. The library's injected
collector is used only in tests. There is no prompt/audio debug sink, text field or credential here.
As in the reference, callers must supply content-free model/reason/type metadata.

`App/Runtime/PerformanceMetricsStore.cs` owns the original chronological 100-row ring buffer and
borrows the current preference document, persistence callback and real persistence-error sink.
`AppSettings.Metrics.cs` retains the exact `cotabbyPerformanceMetricEntries` Data/base64 key.
General reset preserves this app-state key. Record appends before retaining the newest 100 and
persisting. Load/display patch only an existing ID whose corresponding stage is still nil; patches
clamp negative values to zero and never recreate a cleared/evicted row. Initial record values are
retained literally, including zero or negative stages, matching the source. Empty clear preserves
an existing damaged preference blob; nonempty clear removes the key. Loading an oversized valid
blob keeps its tail in memory without rewriting that blob. Windows file-persistence failure keeps
the acknowledged in-memory row and reports through the supplied error sink; the preference writer
is a separate platform boundary.

`Core/Runtime/Metrics/PerformanceMetricEntry` is an immutable canonical value. Missing legacy
source/outcome decode as completion/completed; retired analysis still decodes but is absent from
selectable sources. Failed and cancelled remain distinct. Optional timings distinguish unobserved
nil from measured zero. `PerformanceMetricCodec` preserves the original whole-array failure,
first duplicate known keys and 2001-epoch date shape. Unknown values remain lazy UTF-8 projections;
known values are decoded and validated. Raw input is never logged. DateTimeOffset supplies Windows
date range/tick precision; the source-derived Store cases use exact representable timestamps and
do not establish equality for Foundation dates outside that range or sub-tick resolution.

The shared `Core/Text/FoundationJsonInteger` now serves metric and speech-profile known integers.
It uses the already observed Foundation integer fast path and decimal/exponent fallback: exact
64-bit integer tokens remain exact, small fallback numbers use Double, and large fallback numbers
retain the source bounds/coefficient behavior. A native probe exposed why ordinary Decimal is
insufficient: 1e-100 must be rejected, while a tiny fractional suffix on 1 rounds to 1 in Foundation.
Permanent source cases cover those results and both bare/floating 64-bit boundaries. The existing
vocabulary codec is unchanged. All prior speech preference cases remain regression checks.

The verifier compiles both complete original owners and their actual values/RequestID generator.
It includes the actual LearningRequestContext value declaration; unrelated task-local/display
protocols are not part of this slice. Two Logging imports and two guarded diagnostic bodies are
adapted solely for the standalone executable: the unused default telemetry logger requires the
driver's explicit sink, and unexpected primitive history encoding failure fails the probe. Actual
telemetry events execute through the injected collector; actual UserDefaults and Codable storage
execute in a private temporary suite. No replacement runtime/history state machine is used, and
no original XCTest/Swift Testing suite or model is executed.

The 78 locked observations contain 32 telemetry traces, 45 Store traces and one metadata trace:
25 original-test adaptations and 53 source extensions. They compare complete runtime state/labels,
all chronological entries, raw preferences, structured event order/fields, returned row identities
and tracking-getter counts after every action. Generated row/correlation IDs retain equality and
joins through symbolic identities; fixed seed UUIDs and user request IDs remain literal. Driver
returned IDs include rows already evicted before a snapshot, so eviction cannot leave random IDs
in expectations. Uninjectable telemetry wall time alone becomes a captured marker; controlled
monotonic durations and explicit Store dates remain exact. JSONSerialization's raw preference
projection materializes NSNumber (exact signed/unsigned integers, otherwise Double), rather than
preserving arbitrary number text. Invalid raw data retains its exact base64.

Three additional cases check canonical owned runtime sets, retained history/reset bytes and
read-only events plus Windows persistence failure. Local checks pass: 78 source observations (also independently verified), 81 focused telemetry
cases plus all 147 existing speech-preference cases, full Core 4,420 and App 986 tests. The Release
solution cross-build passes with zero warnings/errors. Both final-head CI jobs with all 62 source
verifiers remain required. UI-context composition, actual helper PID/reaping, controller/player/provider/performance
UI, localized labels and native Windows desktop evidence remain open. No package, installer,
artifact upload, model download or new transmission is introduced.
