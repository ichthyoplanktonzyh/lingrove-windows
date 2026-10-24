# Shared speech controller and export ownership

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/LearningSpeechController.swift` and `LearningSpeechController+Export.swift`,
`Models/Speech/SpeechGenerationProfile.swift`, and the original controller/generated-controller tests.
This ports the existing owner and its workflows. It supplies no installed voice catalog, native
recognizer, synthesizer, encoded codec/player, PCM device, generator, model transfer manager,
AppHost registration, settings UI or card/review-audio composition.

`Core/Speech/SpeechRequest.cs` holds card-owned target identity, clicked text, recognition-only
context and explicit automatic/fixed language intent. Canonical equality matches Swift strings.
`RuntimeModelOption` preserves existing model labels. `SpeechExportIdentity` owns the exact sorted
Foundation profile/string encoding used for fingerprints, independently of platform effects.
`Inference/Speech/SpeechAudioGeneration.cs` freezes the bounded clicked text, resolved language,
source and request sampler for an independent generator. Audio requests cannot be JSON serialized;
credentials remain ephemeral and outside fingerprints. Recognition context has no field in this
request, so it cannot accidentally become spoken text or endpoint input.

`App/Speech/LearningSpeechController` is a long-lived UI-context owner borrowed by cards and review
sessions. Its constructor requires every platform boundary. The host will own settings, telemetry,
providers, codecs, generator and model manager once; views submit detached targets. There are no
fake production defaults. The controller extension owns explicit export admission, captured source,
identity and caller cancellation. The narrow model-management contract exists because lazy inventory
and protected filenames belong to the platform manager, while selection policy belongs to this owner.

## Preserved behavior

A click first checks export exclusivity, toggles its current target off, retires old playback, checks
4096 UTF-8 bytes/1000 graphemes, sanitizes text and resolves language locally. Context helps recognition
but is discarded before provider work. A normal click detects language; an explicit audition can
retain its fixed language. System voice enumeration is lazy. Missing language/voice/configuration
remains a visible target-scoped failure, with no provider fallback.

Generation captures endpoint/key/voice or paired local assets, sampler and playback rate once. Local
PCM preparation begins before generation; ordered chunks await the player and carry backpressure.
Encoded data goes through the required codec/player. Generation EOF is independent of audible drain.
Only the current token may update playback, error or telemetry state. Scoped Stop/dismissal cannot
clear another card's state. Model selection preserves known Qwen companion priority and clears an
old decoder when its required pair is absent; custom imports keep their manual decoder. The lazy
manager protects selected files plus runtime-resident files until the runtime PID is cleared.

Export configuration identity includes revision, raw destination/asset preferences, ordered voices
and profile bytes. A prepared request freezes source, recognized language, selected voice and
fingerprint; its key is excluded. Export and playback are mutually exclusive. Stop retires playback
but does not cancel an export. The review caller owns export cancellation, joins it, and checks the
captured configuration identity before adopting audio. The bounded collector normalizes generated
chunks, then the required decoder validates finite positive duration at most90 seconds. Export
borrows the same generator; it creates no second warm runtime.

Mac MainActor becomes a required serial UI SynchronizationContext. Public actions and published
state belong to its owning thread. Generator receivers can arrive on workers and marshal to UI;
provider/player/model callbacks must already satisfy their documented UI contracts. Borrowed adapters
must return promptly and own blocking/native work themselves. Immutable catalog/model snapshots
cannot change when a provider mutates its inventory.

Controller-owned generation/cancel/shutdown tasks reserve their handles before execution and remain
tracked through cancellation. Throwing .NET cancellation callbacks are collected while cleanup
continues, then reported at DrainAsync after accepted work joins. Weak static callback factories
prevent borrowed providers/settings/model callbacks from retaining the owner. Dispose rejects an
active caller-owned export; after callers cancel/join it, Dispose stops, drains and detaches callbacks.
PCM device/facade drain and borrowed model-manager/native-adapter drain remain their own host lifetime
responsibilities. The controller does not claim to join external exports or player-internal work.

## Source evidence and limits

`fixtures/parity/speech-controller-v1.json` contains104 observations:42 exact Foundation profile
byte/digest cases,14 exact controller configuration-identity traces, and48 full controller flows.
There are22 adaptations of21 original test methods (the clicked-text method has two context variants)
and82 source-policy extensions. Both runners lock version/pin/the fixture reference-locale field (en_US),104 unique IDs and their hash,
scope/provenance counts, source labels and method-reference hash. The native script checks those
original methods exist, separately asserts the Swift runtime locale is en_US, and requires exact output
count and ID order. These are adapted scenarios;
they do not execute or claim passage of the original Swift Testing suite.

The verifier compiles unchanged complete original controller/export, settings, telemetry, encoded
player and language-policy bodies, plus original PCM facade/ledger and required value types. It uses
the verbatim original FakeSpeechProvider, PendingSpeechGenerator and TestPCMPlayer. Default native
provider/generator/renderer constructors are fatal unused seams. Explicit SDK shadows replace only
AVAudioPlayer/device behavior and NaturalLanguage hypotheses; they are no real audio/recognizer
proof. The system exporter returns scripted audio through the original WAV policy. The model wrapper
contains only unchanged original installedModelOptions/isModelInUse and full GGUF-discovery method
bodies; it is not a model-download implementation. Imported telemetry/player reference adapters
retain their documented logger/platform boundaries.

Each flow compares active/error/phase/catalog/resolved request, provider starts/stops, frozen generator
requests, cancellation, PCM chunks/EOF, encoded effect calls/duration/rate/byte digest, model laziness/
protection, prepared exports, export token/revision and telemetry model/engine/phase/details. Only
random token values become stable first-appearance aliases. Fixture-owned temporary paths become
`$ASSETS`, including macOS /var versus /private/var aliases. Local export fingerprints include absolute
platform paths, so their equality/change relation becomes an explicit alias; frozen source paths
still compare. System/endpoint fingerprints, all42 profile bytes and14 configuration-identity traces
remain exact. Telemetry request/operation/source correlation fields are outside these flow
observations; the separate telemetry slice checks individual context fields, not controller joins.
Only diagnostic exception type names become shared categories; error messages/outcomes are retained.
A fixed clock produces exact deterministic timings. No production policy is rewritten for the probe.

Both drivers join admitted asynchronous actions before observing them. Cleanup captures the retiring
audio task before shutdown clears its handle, releases generator gates and joins exports/tasks before
the UI context closes. Expectations come from the source executable. Native verification passed104,
including an independent read-only run. The real C# owner passes those104 cases plus four adapter
facts: canonical/ephemeral values, throwing-cancellation cleanup, caller export/disposal ownership,
and detached inventory snapshots. A compiled WPF case exercises worker audio marshaling, settings
invalidation and stale cancellation on its actual STA dispatcher with explicit in-memory effects.
Full portable suites pass App1133/Core4420 with no failures or skips; the Release solution cross-build
passes with zero warnings/errors. WPF execution remains Windows-CI-only. The64th Mac CI verifier
includes all required sparse source/test paths. Both final-head CI jobs
remain required before merge.

## Remaining integration

Required Windows voice/recognition/collation, audio codecs/renderers, system recording, independent
local helper/endpoint generator and real model inventory/transfer remain work, followed by shared
AppHost/settings/card/review-audio composition. Hardware audio, language detection, device loss,
pitch/rate, latency, UI dismissal and normal application shutdown need native evidence. Source traces,
portable tests and a compiled STA test do not establish audible or whole-product parity. This slice
creates no package, model download, new transmission or application-visible speech action.
