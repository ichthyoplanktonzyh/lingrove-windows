# Review recording preparation and export contracts

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Review/ReviewAudioPreparation.swift`, `Cotabby/Models/Speech/SpeechExportModels.swift`,
and speech/recording values in `LearningSpeechModels.swift` and `ReviewAudioModels.swift`.
This is a preparation library, with no review sheet, provider or media upload connected.

One App `ReviewAudioPreparation` belongs to one future sheet. It borrows `ISpeechExporting`,
keeps its own UUID and independent expression/sentence recordings, and publishes an immutable
dictionary snapshot. Request construction passes the chosen side unchanged, entry language and
same-side sentence as local recognition context; only an exactly empty sentence becomes null.
The speech controller will clean/resolve/freeze these inputs. Preparing a matching fingerprint
returns cached audio before checking busy/cancellation. Force skips reuse. A new operation stops
this owner's audition, marks one generating slot, awaits export, checks cancellation before writing
the result, and always clears generating. Failure leaves any prior recording intact. Audition uses
the prepared bytes and request language, without another synthesis when the cache matches.

Discard stops preview and releases cached references; it does not cancel generation. The future
sheet must cancel its accepted task when closing. As in the source, an uncancelled held export can
publish after discard; a cancelled one cannot. Cached prepare/audition also precedes cancellation.
These behaviours are measured explicitly, not replaced with a new implicit cancellation owner.
Calls belong to one UI context; awaits retain that context like the original MainActor. No background
caller may mutate the owner concurrently. The owner writes no temporary audio or persisted receipt.

Core owns the detached prepared recording, unique playback token and fixed actionable error values.
The frozen export request, closed system/generated and local/endpoint source variants, local codec
rate and `ISpeechExporting` live in Inference: the existing validated endpoint type already lives
there, so Core gains no transport dependency. The shared immutable sampler is reused. Construction
performs no synthesis, file admission or network operation. Generated requests retain their chosen
source/sampler and ephemeral key; context is absent from the export value. Source classes have no
automatic record diagnostics, and the key is excluded from JSON. Fingerprint computation and actual
configuration/file/voice validation remain controller/settings work.

`verify-review-audio-macos-parity.py` compiles the complete original preparation class, export
models, learning-speech models, sampler and endpoint models, plus actual notebook/review values.
Its MainActor fake implements only speech effects and held continuations. Forty-five source-derived
preparation traces compare every outcome, generating/cache snapshot, byte hash and cumulative
request/export/stop/audition call. They cover both sides/all languages, exact context, reuse/force,
independent slots, canonical fingerprints, failure recovery, busy admission, cache-before-busy,
pre-cancelled cache, ignored cancellation, held completion and discard/stop while generating.
One additional trace observes all thirteen original error cases, including five HTTP statuses.
No original Swift Testing suite or real synthesis runs. Fake bytes deliberately test ownership only
and are not playable audio. Expected results were captured from that executable; IDs are locked.

The source process pins/asserts en_US for the English error snapshot. Initial focused tests caught
Foundation's grouped integer interpolation at Int32.MaxValue; the English Windows formatter now
preserves that observation. Expectations were unchanged. Full Windows interface localization remains
work. C# runs all46 traces plus three ownership/frozen-secret/token cases; all49 focused cases passed.
Full Core4418 and portable App667 suites passed with zero failed/skipped. Release solution
cross-build passed with zero warnings/errors. Native verification passed locally and independently;
both final-head CI jobs remain required before merge.

This slice adds no AppHost speech owner, request fingerprint factory, voice/file admission, Windows
speech effects, local/endpoint generator, player, preparation window or Anki send transaction.
Native timing/device/codec behaviour and real Anki media remain unverified. No audio file, socket,
model download, installer, release or uploaded artifact is produced.
