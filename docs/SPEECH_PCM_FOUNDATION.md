# PCM admission and playback accounting

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, the complete
`SpeechPCMChunk` declaration in `Cotabby/Models/Speech/SpeechAudioChunk.swift` and complete
`Cotabby/Support/Speech/SpeechPCMPlaybackState.swift`. This is a portable speech foundation, with
no player, generator, helper process, endpoint or UI connected.

`SpeechPCMChunk` admits one mono Float32 little-endian block: 8,000–192,000 Hz, a nonempty multiple
of four bytes, at most 262,144 bytes, every sample finite and magnitude at most eight. It freezes
caller-owned bytes before inspecting them, then publishes only copies. Value equality/hash include
exact bytes and rate; positive/negative zero remain distinct byte values. Invalid input reuses the
existing fixed `SpeechExportException(InvalidAudio)` wording for the original `SpeechError.invalidAudio`.
It does not silently rescale, clip, reinterpret the format or schedule an audio device.

`SpeechPCMPlaybackState` is one mutable ledger owned by a serial playback boundary. The Swift
struct becomes an explicitly owned C# reference type; callers must serialize it and create a new
ledger for another utterance. Only sample counts/IDs are retained. Each enqueue returns a monotonic
ID, adds queued and lifetime samples, and enforces the original 11,520,000 lifetime-sample budget.
A played callback removes its ID once; duplicate or unknown acknowledgements have no effect.
Draining a queue does not reset the lifetime budget or mean generation has ended. EndInput requires
prior accepted samples and can occur only once. Completion requires both EndInput and an empty queue.

CanStart requires positive queued samples, sample rate and playback rate. It releases final short
utterances at EOF; otherwise at least half a second of audible audio must be buffered. Source
floating-point behavior is preserved: NaN/nonpositive rates fail; positive infinity cannot start
before EOF but a queued final utterance can start after EOF. The rule does not impose a new rate
range that belongs to the later playback/settings boundary.

`verify-speech-pcm-macos-parity.py` compiles both complete original declarations, with only the
referenced `SpeechError.invalidAudio` error case supplied as a stub. Sixty-four actual-source
observations cover rate/size boundaries, Float32 infinities/NaNs/subnormals/magnitude, fragmented
logical queue progress, out-of-order/duplicate/unknown callbacks, empty/repeated EOF, lifetime budget
and extreme Int64/double inputs. Chunk observations retain byte count, sample count, rate and SHA-256;
queue traces compare every state, result/ID and error category. No exercised rule is rewritten in
Swift. Expected results were captured from this executable; reviewed case IDs are locked in both
runners. These are policy extensions derived from source responsibilities, not claims of executing
the original Swift Testing suite.

Core tests run the same64 fixtures and two additional detached-byte/equality and independent-ledger
cases. The first local test invocation exposed a fixture copy-path mistake; the runner now uses the
existing `fixtures/parity/` output location, with unchanged expected results. Source verification, all66 focused cases and the full Core3786 suite passed with zero failures/skips.
Full Release solution cross-build passed with zero warnings/errors. Final-head Windows/macOS CI
remains required before merge.

The existing spoken-text and encoded export policies have their separate
[text](SPEECH_TEXT_POLICY.md) and [export](SPEECH_EXPORT_POLICY.md) evidence. Pipe framing, process
lifetime, real playback/callback serialization, underruns, device loss, installed voices, speech
settings/language/provider selection, system/local/endpoint synthesis and review-audio integration
remain work. This round does not establish audible or native runtime parity and produces no package,
model download, audio file or uploaded artifact.
