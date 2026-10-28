# Windows encoded speech decoding

Behavior reference: Mac `SpeechAudioPlayer.swift` and `LearningSpeechController+Export.swift` at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, unchanged at current stable
`cc468b89f5c360cc7210fac4f1a8b52197870bf2`. Their native decoder validates finite, positive,
at-most90-second audio before playback or review export. This slice supplies the Windows codec
and export-duration adapter. Encoded playback and AppHost activation remain separate integration.

## Native and managed ownership

`WindowsSpeechAudioCodec` opens nothing at construction. Each explicit decode freezes at most12MiB
of already received WAV/MP3 bytes, then uses a joined `WindowsSpeechMta` operation. That dedicated
thread initializes COM and Media Foundation, creates a native memory IStream, wraps its byte stream
and opens a synchronous source reader. No URL, temporary file, playback device, synthesizer or
model is used. Header admission rejects media playlists; native codec support still depends on
the installed Windows media components.

The native half uses [Microsoft's source-reader flow](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--decoding-audio)
and [memory-byte-stream reader](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-mfcreatesourcereaderfrombytestream).
The original sample rate and mono/stereo channel count are explicitly negotiated. Float32 output
is preferred; a decoder exposing PCM16 converts to Float32 after reading. Samples must be finite
and within the existing PCM amplitude bound. Format changes fail closed. Channel order stays
interleaved; review audio is not silently downmixed to the helper's mono contract.

Returned decoded frames, not an untrusted duration header, establish duration and the90-second
ceiling. Each native sample is bounded before copying. A single8192-frame accumulator spans native
reads, so tiny samples cannot allocate an unbounded number of packet objects. Maximum decoded
storage is90×192000×2×4bytes plus one packet and bounded list overhead; duration-only inspection
validates/counts and discards output. One detached `WindowsSpeechDecodedAudio` transfers completed
packets to future playback without retaining a COM reference.

`WindowsSpeechAudioCodec.Native` owns every returned interface reference immediately, even if the
next initialization call fails. Locked buffers always unlock before release; reader, byte stream,
memory stream and Media Foundation retire before COM uninitialization on their owner thread.
Cancellation is checked between reads and bounded sample conversions. The task then joins the
actual thread; cancellation never abandons a native operation. A synchronous Windows codec call
cannot be forcibly interrupted while inside the OS.

The private build-time [CsWin32 generator](https://github.com/microsoft/CsWin32/tree/6f3334302499fcbfea98ec3b433da1b2c49bfb47)
supplies complete SDK signatures and COM layouts, avoiding handwritten vtable offsets and private
NAudio interfaces. Version0.3.367 and its SDK metadata dependency are pinned by package resolution.
Generation runs before WPF temporary compilation. Generated files stay in ignored `obj/`; no SDK
metadata/tool DLL is an application runtime dependency. The generator MIT license and attribution
are retained in ordinary application output. NAudio remains the separate PCM output/DSP adapter.

## Export lifetime and evidence

The existing `ISpeechAudioDuration` gains an asynchronous default which preserves synchronous
source/test adapters. Windows overrides it with the real joined decode. The shared controller
awaits duration and rechecks cancellation/token before publishing exported audio; a cancelled
export retains exclusivity until its accepted native decode returns. This is a platform scheduling
adapter, with the same export result and admission policy. UI uses the asynchronous seam.

Portable checks cover held-decoder cancellation/exclusivity and the unchanged original controller
observations. The full portable App suite passes1221 cases, with no failures/skips. The Release
Windows-targeting solution compiles with zero warnings/errors. Three Windows component checks
execute actual stereo WAV and generated-tone MP3 decoding, invalid/overlong media and recovery,
plus cancellation/join of an accepted MTA operation. Their execution requires final-head Windows CI;
a cross-build alone does not establish successful native decoding.

Native codecs may differ in compressed-audio padding and availability from AVFoundation; this is
not a claim of codec-bit or perceptual identity. Playback speed/stereo output, device events,
system voices/export, model-backed generation, application/card/review wiring and physical audio
acceptance remain work. No installer, release, model download or new transmission is introduced.
