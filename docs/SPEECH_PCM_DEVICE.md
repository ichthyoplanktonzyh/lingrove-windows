# Windows PCM output device

Behavior reference: complete Mac `SpeechPCMRenderer.swift` and `SpeechPCMPlayer.swift` at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, unchanged at current stable
`cc468b89f5c360cc7210fac4f1a8b52197870bf2`. This adapts their serialized audio graph, preparation,
revision-scoped buffer callbacks, reset/reuse and device-loss responsibilities. It does not activate
speech in AppHost or claim audible parity.

## Ownership and data flow

The UI-owned App `SpeechPCMPlayer` keeps the original readiness/events/backpressure ledger. The
Win32 `WindowsSpeechPCMInputAdapter` borrows that facade and splits a legal helper block into
at most 250ms fragments before the ledger assigns buffer IDs. Slow tempo processing retains an
analysis window: acknowledging a whole multi-second helper block could otherwise wait for future
input that the same blocked producer cannot supply. Fragmentation keeps the original two-audible-
second backpressure, one continuous processor and exact input bytes. Cancellation and a local epoch
are checked around every await; an old producer cannot clear a newer producer's overlap guard.

`WindowsSpeechPCMRenderer` owns one serial worker for revision, graph identity, tempo processing,
source-buffer endpoints and the output-tail barrier. The [tempo processor](SPEECH_PCM_TEMPO.md)
runs here, away from UI and the audio thread. A separate `IWindowsSpeechOutputDevice` owns detached
packets and native playback. Source-buffer callbacks use a conservative initial-processing-window
margin before EOF; after EOF they are capped to actual produced output. Finished also requires the
separate actual-output barrier. Intermediate DSP offsets are conservative estimates, not a claim of
sample-exact correspondence to Apple's time-pitch node.

`WindowsWasapiSpeechDevice` owns a dedicated MTA thread for COM initialization, endpoint activation,
render buffer leases, raw clock reads and all native release. It uses unmodified
[NAudio.Wasapi 3.3.0](https://github.com/naudio/NAudio/tree/4f8889e2bd34c3978a25652c6c172fa1502ccba3)
low-level wrappers. Shared event-driven Float32 mono output allows the Windows audio engine's
sample-rate conversion; tempo changes preserve pitch through SoundTouch. The client opens only
on explicit preparation, warms with silence, then stays stopped during idle. Same-rate valid graphs
are reused; rate change or invalidation releases the graph before replacement.

The device uses the render/multimedia default endpoint. Endpoint change, removal, inactive state,
format change and audio-session disconnection mark an atomic invalid bit and wake its owner.
Session notifications also cover a stopped stream. These callbacks perform no COM teardown or UI
work. Ordinary volume changes do not interrupt playback. An abrupt audio-service crash may omit
notifications; a subsequent native call then reports failure and retires the graph.

## Playback progress and retirement

`WindowsSpeechOutputTimeline` maps authored frames to device positions. Generation gaps add silence
to the device stream, so elapsed device time cannot acknowledge later authored PCM. After an
underrun, future writes re-anchor to the running cursor plus padding and a conservative endpoint-
buffer margin. A reset clears both coordinate systems. Startup prefill uses the known reset-zero
cursor; it acknowledges no audio. Running progress requires a successful raw clock observation,
never QPC extrapolation. Transient missed readings defer writes/acknowledgements; persistent failure
invalidates the device. The clock describes endpoint-stream progress, not acoustic measurement.

Relevant native contracts are Microsoft's [shared initialization requirements](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize),
[same-thread render leases](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiorenderclient-getbuffer),
[reset-zero stream clock](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-reset)
and [session disconnection](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionevents-onsessiondisconnected).

Stop retires renderer identities and settles the old output barrier before native reset discards
packets. Graph invalidation faults the active barrier before retiring it; delayed progress from an
old epoch or graph cannot finish a new utterance. Worker-to-UI notifications are asynchronous and
tracked. Shutdown must stop/join producers, shut down and drain the facade, then dispose the
renderer before releasing UI. Renderer disposal joins accepted worker work, native cleanup, the
actual MTA thread and notifications. Cancellation cannot abandon native work. Synchronous driver
calls cannot be forcibly interrupted in process; cancellation is observed between startup calls.

## Evidence and remaining integration

Component checks execute the actual facade, input adapter, SoundTouch processor and renderer over a
controlled output seam. They cover largest legal 65536-sample blocks at 8/24kHz and 0.5/1/1.5 speed,
continuous packet equivalence, new-epoch producer guards, gaps/underruns, stale progress, held device
release and invalidation/recovery. The full portable App suite passes 1220 cases. The Release
Windows-targeting solution compiles with zero warnings/errors. These checks exercise no speaker,
microphone, model, endpoint, or desktop.

Concrete WASAPI startup, physical devices, unplug/default replacement, session/service loss,
audible tails/gaps/stop, CPU contention and perceptual pitch/speed comparison remain Windows-host
acceptance gates. Installed voices, encoded playback, synthesis/export, model management,
AppHost/settings/card/review composition remain work. NAudio's complete MIT license and notice,
alongside the separate dependency DLLs, are retained in ordinary build output. No package, model
download, release or new transmission is introduced.
