# Original in-memory audio export

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

`Core/Speech/SpeechExportPolicy.cs` adapts the complete original
`Cotabby/Support/Speech/SpeechExportPolicy.swift`. `SpeechExportAudio.cs` adapts its detached byte/
extension value from `Models/Speech/SpeechExportModels.swift` and the two export failure cases
from `LearningSpeechModels.swift`. No speech generation/controller, voice, playback device, file
read/write, Anki media operation or automatic action is introduced.

## Ownership and byte flow

The future speech export owner supplies bytes in memory. `SpeechExportAudio` copies input and
returned buffers so caller edits cannot change its content-derived identity. Constructing this
value alone establishes no codec validity. The stateless policy admits/normalizes encoded bytes;
review preparation can borrow the resulting value without borrowing a runtime/player or key.
The export exception carries only a typed reason and the original fixed English error message.

Raw little-endian Float32 mono PCM requires nonempty aligned data, the original12 MiB-minus-header
input budget, sample rate8000...192000 and at most90 seconds. Finite samples clamp to[-1,1], scale
by32767 and round ties away from zero, preserving the source's-32767 negative endpoint. It emits
canonical little-endian PCM16 WAV with the original RIFF/fmt/data header.

Encoded input first enforces the original nonempty/12 MiB cap. RIFF/WAVE parsing retains only the
bounded format and final data chunk: unknown chunks/padding are skipped, later fmt/data wins and
an incomplete trailing fragment follows source behavior. PCM16 mono/stereo is rewrapped with a
canonical header; Float32 mono converts through the raw path. Other formats reject. RIFF's declared
whole-file size is not newly enforced. MP3 keeps the original ID3/MPEG header admission and bytes;
this is not a full MP3 decoder, duration check or proof of audible output.

Digest/filename use lowercase SHA-256 of final encoded bytes, never words or paths. The review
freshness helper now calls this shared digest implementation. The separate valid-filename rule
accepts only the exact original hash-name format and wav/mp3 extension; it performs no disk lookup.

## Source-discovered correction to retained filename admission

Actual execution exposed a Foundation storage-dependent difference: the same filename UTF-8 bytes
reject final LF/CRLF/other newline scalars when supplied as a native Swift/JSONDecoder string, but
the older JSONSerialization/NSString bridge admits one final newline. The earlier archive verifier
used only that bridged input and Windows reproduced its lenient result. The notebook and Anki
JSONDecoder paths and generated names use native strings, which are the adaptation boundary here.

The archive verifier now supplies native JSONDecoder strings and retains separate `bridgedExpected`
observations for all16 filename rows, with a byte-equivalence precondition. Exactly eight native
expected decisions were recaptured from the unchanged original method; their earlier bridge
observations are preserved, not discarded or normalized. No other expected decision changed.
Archive receipt validation now delegates to the shared strict native-path rule. Stored names are
never trimmed/rewritten. This establishes the native path, not universal behavior across every
NSString storage representation inside Foundation. Pinned durable rows decode in
`Models/Vocabulary/VocabularyDatabaseModels.swift` Payload.decode; backup restore decodes in
`Services/Vocabulary/VocabularyArchive+Media.swift`; the Anki client decodes in
`Services/Review/AnkiConnectClient.swift`. All three use JSONDecoder.

## Evidence and remaining boundaries

`speech-export-v1.json` contains73 source-policy-extension observations. The verifier compiles the
complete original exporter and exact original audio/error declarations; driver code only constructs
synthetic byte inputs and projects results. Both runners lock the reviewed case-ID matrix. Cases
cover PCM rounding/clamping/nonfinite input, alignment/rate/duration/request limits, mono/stereo/
float WAVs, duplicate/unknown/malformed/missing chunks, declared-size overflow, trailing fragments,
MP3 header branches, exact encoded-byte budgets, identity-only values and filename anchors/case.

Outputs compare full byte count, SHA-256, extension and filename; small results additionally compare
every byte in hexadecimal. Two extra .NET facts test buffer ownership and independently read the
canonical header/scaling, including normalize-again equality. Filename cases also exercise the
actual archive admission path. Hash/count observations avoid embedding megabytes of zero audio in
fixtures. This evidence establishes byte policy, not a model's generated audio, native decoding,
playback, stored media identity or an Anki transaction. Unicode/locale and socket/device gates from
other slices remain open; error localization remains future presentation work.

Local validation passes75 focused cases, all3719 Core and269 portable App tests without failures/
skips, both73 export and124 repaired validation source observations, and full Release cross-build
with zero warnings/errors. Final-head Windows/Mac47-verifier CI remains required before merge. No installer, artifact upload, model
download, audio device or network test is introduced.
