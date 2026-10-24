# Independent local speech runtime

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/LocalSpeechRuntime.swift`, `SpeechAudioGenerator.swift`,
`CotabbyTests/Services/Speech/LocalSpeechRuntimeTests.swift`, and their original models,
frame decoder, local-language inventory and request sampler. The native verifier executes the
complete runtime; generator routing is checked separately by portable adapter tests.

`Inference/Speech/LocalSpeechRuntime.cs` owns one lazy helper and one active token. Short lock
turns adapt Swift actor turns; pipe IO, receiver callbacks, telemetry and native waits do not hold
that lock. `SpeechHelperSession.cs` owns one process's readiness/result waiters and bounded framed
reader. The required process/pressure contracts isolate OS effects. `SpeechAudioGenerator.cs`
routes the frozen source to local PCM or the independent endpoint's encoded response. Cancel by
token and shutdown affect the local runtime; endpoint lifetime belongs to its generation caller.
There is no fallback and no AppHost registration, UI action or model download in this change.

The new token is reserved before old retirement is awaited. All callers share the same uncancelled
retirement barrier. Failed reaping retains the old session and prevents another model from starting;
a later action can retry. Native retirement never waits for a blocked PCM receiver or observer.
The host separately stops/joins generation callers, releases receivers, shuts down and drains the
runtime before destroying its dependencies. Drain joins old readers and background work; an unreaped
child is reported while ownership stays available for retry. Telemetry is ordered per session and
contains immutable runtime metadata. Observer delivery cannot own process/pipe lifetime.

The helper must announce protocol2, use the current uppercase request UUID, monotonic chunk sequence,
mono Float32 little-endian audio, a consistent8–192 kHz rate, bounded finite PCM blocks, at most60
seconds/32 MiB and exact final sample totals. Malformed/truncated streams and receiver rejection fail
the operation and retire its session. One completed request can reuse a resident model with different
request sampling. Defaults remain temperature0.8/top-p0.95/top-k40/repetition1/seed42, max_frames512,
120-second idle unload and180-second operation deadline. Reference audio is an explicit optional path.
Its file/codec admission remains the settings/helper responsibility.

## Windows process and pressure adaptation

`Win32/Speech/WindowsSpeechProcessFactory.cs` creates binary pipes and a NUL stderr sink, passes
individual Unicode arguments without a shell, and launches on a worker. A kill-on-close Job Object
is attached atomically during CreateProcess through
[PROC_THREAD_ATTRIBUTE_JOB_LIST](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute).
Parent pipe endpoints are noninheritable from creation; only child endpoints are duplicated as
inheritable. The attribute list pins the job and exactly three child stdio handles until creation
finishes.
This closes the launch-to-job crash window and avoids inheriting unrelated application handles.

Windows has no detached-console SIGTERM counterpart. Closing the owned stdin handle without flushing
gives the helper one second to observe EOF; termination then waits up to two more seconds for that
exact process. Successful reaping precedes stream/job disposal, and closing the job ends descendants.
Concurrent stops share their task, and a failed wait can be retried. The parent input SafeFileHandle
is retained at stream construction: reading FileStream.SafeFileHandle during stop could flush a
blocked write. Expected errors from abandoning that intentionally closed stdin do not skip cleanup
of stdout/process handles. Native startup failure exposes the fixed speech error, never paths.

`WindowsSpeechMemoryPressure.cs` lazily binds kernel
[low/high resource notifications](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-creatememoryresourcenotification).
A blocking worker waits for low, requests idle-only release, then waits for high before rearming.
This prevents spinning on a level-signaled low-memory event. Shutdown stops/joins the binding off
UI before closing its handles. OS thresholds differ from Dispatch warning/critical thresholds;
physical pressure delivery has no parity claim.

## Evidence and remaining work

The17-row corpus contains11 adaptations of all10 original test methods (invalid-stream has two
arguments) and6 source-policy extensions. Both runners lock the reference pin/locale, ID set/hash,
source/test paths, exact ID-to-original-method references and provenance counts. The native executable compiles the entire unchanged runtime,
frame decoder and request values, appends read-only lifetime observers in the same Swift file, and
uses the original verbatim executable Python fixture. Adaptations execute the scenarios, rather
than the Swift Testing suite. Idle/cancel readiness waits use observed barriers; cancellation during
old drain retains the original150 ms delay. Timing is not benchmark evidence.

Observation compares actual request JSON fields, parameter defaults/normalization, per-request token
matching, delivered PCM block sizes and exact audio SHA256, actual helper start count and reaping.
Only the generated request UUID becomes a per-request equality check and the fixture's reference-audio root
becomes `$ROOT`. Failure is projected to the original test's broad failure contract, with the
specific cancellation-during-drain/pre-cancel checks retained. This is not exact error-message or
complete telemetry-transcript parity. The source fixture contains no model/audio device.

C# runs the same17 observations over a separate test-only executable, with equivalent original
ready/chunk/result/wait/wrong-sequence/truncation effects. Portable tests use an explicit test process
factory; Windows-only inference tests use the production CreateProcess/job/pipes adapter. The test
helper is built only as a test dependency and copied after its project builds, including a clean CI
checkout. It is never copied to the application. Additional portable tests cover failed-reap retry,
lazy idle-only pressure, rejected ID/rate/finite samples/totals, process exit, explicit old-reader
drain and source routing. Windows tests cover graceful EOF, forced reaping, shared/idempotent stops,
descendant job retirement, startup cancellation/failure and repeated pressure-binding disposal without inducing low memory.

The native llama.cpp/libmtmd speech helper, reproducible build and model-backed generation remain
work. Actual audible PCM/system playback, voices, recognition/collation, reference decoding,
AppHost/settings/review integration and physical Windows shutdown/pressure/GPU validation remain
open. Cross-compilation and test PCM do not establish those capabilities or full product parity.

Local validation:17 source observations (including an independent run),30 focused portable runtime/
routing tests, full Core4420 and App1206 tests, and the complete Release cross-build with zero
warnings/errors. Windows native process/pressure tests require the Windows CI job; no local
Windows execution is claimed.
