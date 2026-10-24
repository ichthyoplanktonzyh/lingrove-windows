# Windows speech tempo and EOF ownership

Behavior reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/SpeechPCMRenderer.swift` and `SpeechPCMPlayer.swift`. These two files are
unchanged at current stable `cc468b89f5c360cc7210fac4f1a8b52197870bf2`. The renderer uses
`AVAudioUnitTimePitch.rate` and resets its processing tail between utterances. Windows must preserve
that responsibility; changing a device sample rate would also change pitch.

`Win32/Speech/WindowsSpeechTempoProcessor` is owned once per utterance by the serialized
Windows renderer. It accepts detached validated mono Float32 chunks, processes their continuous
stream away from UI, and returns renderer-owned little-endian output packets at the original sample
rate. It opens no device and owns no generator, file or text. The unmodified managed
[`SoundTouch.Net` 2.3.2](https://www.nuget.org/packages/SoundTouch.Net/2.3.2) dependency supplies the
tempo effect, with `Rate=1`, `Pitch=1` and only `Tempo` changed. Its
[exact package source](https://github.com/owoudenberg/soundtouch.net/tree/98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc)
documents the independent time-stretch operation and streaming latency. This is a platform effect
substitution; it does not claim Apple's DSP algorithm or perceptual output is identical.

Normal speed bypasses DSP and preserves the exact validated bytes. Other speeds retain one
processor across helper chunks; only EOF flushes it. Each library input call is bounded to1024
samples, output packets to8192 samples, output allocation to262144 samples per call, and lifetime
input to the facade's11520000-sample budget. Format/rate/finite-output checks remain fail closed.
Cancellation is checked between bounded calls; it requires retirement rather than retrying a
partially consumed block. Retire clears the processor and rejects later work. Fractional duration
accumulation permits at most one output-sample rounding difference; the device must drain the actual
produced count, never the estimate.

## Tail completion is distinct from generation EOF

The original Mac node owns its effect tail internally. The Windows adapter requires two explicit
defaultable renderer operations while retaining source buffer IDs and revision checks:

1. `CompleteInputAsync` flushes EOF into the device queue before the facade marks its ledger ended.
   Every nonempty input stream calls it, including when earlier source callbacks already emptied
   the ledger. Duplicate/empty EOF is rejected before effect work.
2. `OutputDrainedAsync` registers a nonblocking barrier for all output, including a flushed tail.
   It does not wait for playback inside the method call. Stop/device invalidation must settle the
   old revision's barrier. The facade tracks it before Play, rejects late retired completion,
   and finishes only when both source-buffer accounting and this barrier have drained.

Native graphs whose existing buffer callbacks already account for all output use the completed
defaults. Their original behavior is preserved. A Windows processor cannot acknowledge a tail
merely because EOF was flushed. The usual queue-duration-plus-five-second watchdog still applies;
EndInput returns after scheduling, while the facade owns the output barrier through shutdown.
Cancellation never abandons its underlying task. Host shutdown must join producers, shut down the
facade, drain it, and then release the renderer/context.

## Evidence and remaining work

The unchanged33 original-source facade traces still verify against the Mac executable. Portable
tests execute the actual processor with a generated440Hz tone at0.5×,1× and1.5×, checking duration
and frequency, exact normal-speed bytes, continuous output across different helper packet sizes,
format/cancellation admission, held EOF, a queue emptied before EOF, a separately held output tail,
and joined retirement. No speech model, microphone, speaker or network endpoint is used.

The separate SoundTouch assembly and its full LGPL2.1-or-later notice/license are retained in
ordinary build output under `Speech/Notices/`. The restored package's repository commit matches
the source above; the library is not copied into product source or modified.

The [subsequent Windows device adapter](SPEECH_PCM_DEVICE.md) supplies compiled WASAPI output,
authored/device-position accounting and notification ownership. It is not activated in AppHost.
Physical default-device change/unplug, audible boundaries/underruns, encoded codecs, installed voices,
AppHost/settings/card/review integration and perceptual comparison remain work. Passing generated
tone measurements establishes the tempo mechanism, not audible parity or complete speech support.
