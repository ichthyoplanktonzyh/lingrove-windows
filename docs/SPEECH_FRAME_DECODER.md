# Speech helper frame decoding

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Support/Speech/SpeechAudioFrameDecoder.swift`, including its `Header`/`Frame` declarations.
The source oracle also compiles the complete `SpeechPCMChunk` declaration for its byte limit.
No process, audio device, model, speech controller or product control is connected in this slice.

`SpeechAudioFrameDecoder` belongs to one serial pipe reader. Append copies at most16,384 bytes per
call and retains at most `SpeechPCMChunk.MaximumBytes +20,484` logical undecoded bytes. Next reads a
four-byte big-endian length, admits1–4,096 header bytes, waits for the complete header/payload and
returns only one detached frame. Payload length is0–262,144; a nonzero payload requires event exactly
`chunk`. Failure or incomplete input does not consume bytes. A successful frame alone advances the
buffer. Finish rejects any remainder; it does not permanently close the reusable decoder.

The reader's reusable buffer, immutable header and copied payload have separate lifetimes.
`SpeechAudioFrame.CopyPayload` cannot mutate a previously admitted frame, including after later
frames compact the buffer. The decoder does not validate request IDs, sequence monotonicity,
protocol version, sample-rate/count relationships, format or channels. Those belong to the later
runtime protocol boundary; actual Float32 admission belongs to [SpeechPCMChunk](SPEECH_PCM_FOUNDATION.md).

`SpeechAudioFrameDecoder.Header.cs` owns Foundation-shaped JSON admission rather than coupling it
to a process/player. Required/optional scalar types, nulls, first duplicate keys after snake-case
conversion, root/depth admission and observed encoding/BOM behavior follow the original decoder.
Unknown values are skipped without decoding their strings: the original accepts invalid UTF-8 or
an unpaired escape in an ignored value, but rejects either in a key or known string. Existing
`FoundationJsonInput` consumers still eagerly validate every UTF-8 byte; only this header reader
opts out and performs the required per-key/per-known-string validation. A dedicated regression
also verifies that the Anki wire retains its strict admission.

Integer decoding needs more than a Double cast. Bare Int64 tokens preserve their entire range;
small floating spellings use the observed Double path; large spellings preserve decimal digits,
including the source's truncation of some fractions. Captured boundary cases distinguish raw
`-2^63`, rejected `-2^63.0`, accepted `-(2^63-1).0`, rejected boundary fractions and rejected positive
max-decimal forms. The implementation reads coefficient/scale without rounding a long suffix
through System.Decimal. It preserves these measured Foundation rules rather than imposing a new
mathematical integer grammar. This is bounded evidence against the pinned source/current validation
SDK, not a claim about all Foundation versions.

Framing/payload bounds retain the existing fixed InvalidAudio exception. JSON/header failures use a
fixed `JsonException("Invalid speech frame header.")` without an inner exception or raw data.
This adapts the platform diagnostic wording while preserving the decode-versus-audio error category;
no header data can leak through native decoder diagnostics.

## Evidence

`verify-speech-frame-macos-parity.py` compiles the complete original decoder and PCM declaration,
with only the referenced `SpeechError.invalidAudio` case stubbed. It invokes Append/Next/Finish on
synthetic bytes and records122 traces: byte fragmentation, coalesced frames, partial EOF/retry,
append/aggregate/header/payload limits, header shapes/types/duplicates/snake case, encoding/Unicode,
512-container depth admission and integer/decimal boundaries. Unexpected error types rethrow;
only actual DecodingError and the exact audio case are classified. Header fields, payload byte
count/SHA-256 and every action result/error compare with C#. Expected values were captured from
this executable, not derived from the C# implementation; case IDs are locked in both runners.
These source-policy extensions do not claim to execute the original Swift Testing suite.

Core runs all122 fixture traces plus two detached-payload/shared-admission/privacy cases. All124
focused cases passed after the numeric boundary review. Full Core3910/App582 suites passed with
zero failed/skipped, and Release cross-build passed with zero warnings/errors. Independent review
and both final-head CI jobs remain required before merge.

This does not establish subprocess startup/shutdown, backpressure scheduling, real helper output,
protocol/session validation, decoding/playback, installed voices, synthesis quality or review media
integration. No audio file, model download, socket, installer, release or uploaded artifact is used.
