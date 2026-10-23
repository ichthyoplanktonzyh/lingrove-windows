# Request-owned speech export buffer

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/SpeechExportBuffer.swift`, `SpeechAudioChunk.swift`, the
`SpeechExportAudio` declaration in `SpeechExportModels.swift`, and `SystemSpeechRecording` in
`SystemSpeechAudioExporter.swift`. Byte packaging uses the already adapted complete
[SpeechExportPolicy](SPEECH_EXPORT_POLICY.md).

## Ownership and execution

One explicit export request owns `Cotabby.Inference.Speech.SpeechExportBuffer`. It accumulates
ordered generator chunks and packages an encoded result for its future controller. Core's closed
`SpeechAudioChunk` alternatives carry copied encoded bytes or an immutable admitted PCM block.
`SystemSpeechRecording` carries copied raw system-recording bytes/rate; it deliberately does not
apply the helper block's smaller size/amplitude rules. WAV admission remains the export policy's
responsibility. None of these values owns a voice, file, device, endpoint or model.

The Swift actor becomes worker operations serialized by a private gate. Task.Run keeps normalization
and WAV encoding off a WPF caller's thread; locking protects every mutation and package/finish
operation. An ordered generator awaits AppendAsync before sending its next block. Simultaneous
callers are serialized without promising invocation order, just as callers must respect the
original generator ordering. There is no persistent thread or OS resource to dispose; the request
releases its managed accumulator/bytes when no operation or owner retains it.

Append checks its caller's cancellation token on the worker before inspecting the chunk. Core policy overloads read the frozen values internally without cloning a body before size
admission; the mutable accumulator is read only while its serial gate is held. Producers still
must bound acquisition before constructing a detached value, whose initial ownership copy is
intentional. Encoded
bytes require no prior PCM/encoded result and pass through the original encoded admission. PCM
requires no encoded result, the same sample rate, and cumulative Float32 bytes at most
`MaximumBytes -44`. Source error categories are preserved: mixed PCM-after-encoded, rate mismatch
and byte overflow report ResponseTooLarge; encoded-after-PCM or another encoded response reports
InvalidAudio. Failed admission preserves the prior accumulator.

Package checks cancellation and produces WAV from an independent system recording without seeding
or changing the generator buffer. Its work still uses the same serial gate. Finish has **no**
cancellation check: it returns the admitted encoded value or packages accumulated PCM, rejecting a
missing sample rate. It neither clears nor seals the accumulator. Repeated Finish and later Append
retain the source behavior. Byte admission occurs at Append, while WAV duration/format checks occur
when packaging; the implementation does not move those checks earlier or change their errors.

## Evidence

`verify-speech-buffer-macos-parity.py` compiles the complete original actor, complete audio-chunk
models and export policy, actual export-audio/recording declarations, and the full original
SpeechError enum. A same-file read-only actor extension observes private bytes/count/rate/encoded
state without replacing any mutation. Synthetic input creates actual chunks/recordings. Cancelled
cases cancel a child task before awaiting the original actor; cancelled Finish is measured rather
than assumed to throw. No system synthesizer or endpoint implementation is compiled/executed.

Thirty-five actual-source traces compare every result/error and private-state snapshot, including
byte count, sample rate and SHA-256 of PCM/encoded/exported data. The matrix covers joins, repeated
finish/append-after-finish, encoded WAV/MP3, failures/recovery, mixed modes, rate mismatch, exact
12 MiB allowance, deferred duration failure, independent package operations, cancellation priority,
raw system recordings and PCM admission before the actor call. Codable omission is normalized only
to explicit null in this observation envelope. Expected results were captured from the original
executable; case IDs are locked in both runners. These policy extensions do not claim original
Swift Testing-suite execution or real synthesis.

Portable App tests use the actual Inference owner for all35 traces and add concurrent append/package
coverage. Core separately verifies detached producer/recording values and that oversized values are rejected
without another full-body allocation. The C# test's private-state
reflection is observational, matching the actor extension without adding a product diagnostics API.
All36 focused App cases and the35 original-source traces passed. Full Core3912/App618 suites
passed with zero failed/skipped, and Release cross-build passed with zero warnings/errors.
Independent production/evidence reviews are clear; both final-head CI jobs are required before merge.

This slice has no AppHost/control wiring, system voice provider, native callback sink, subprocess,
request/profile/voice selection, speech playback or review media upload. It does not establish
native thread/callback/device behavior or synthesis quality. No audio file, socket, model download,
installer, release or uploaded artifact is produced.
