# Speech preferences and destination profiles

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Models/Speech/LearningSpeechSettings.swift`, `SpeechGenerationProfile.swift`,
`Cotabby/Models/Runtime/Parameters/ModelParameterSettings.swift` and
`Cotabby/Support/Prompting/ModelContextBudget.swift`. This adapts the existing preference owner;
it adds no speech command, settings pane, AppHost service, synthesizer, model load or HTTP request.

`App/Speech/LearningSpeechSettings.cs` owns one UI-context preference projection and borrows the
current AppSettings document, persistence callback, credential store and file inspector. The ten
original `learningSpeech.*` keys live in `Settings/AppSettings.Speech.cs`; raw JSON preserves future
or damaged values until an explicit edit. Data values use the existing Windows base64 convention.
Speech keys survive general reset because the original suggestion reset excludes them. The owner
must be constructed once by the future composition root. A separate speech DPAPI namespace must
be explicitly injected there; this library requires credentials and never defaults to the
completion/translation key store. Construction, configuration and sampler access do not read keys.
Explicit key save propagates failure before invalidation/publication; explicit read propagates its
failure. Keys never enter AppSettings or a generation profile.

`SpeechParameterSettings` specializes the original generic per-destination map for the existing
speech value. It distinguishes a missing profile from a present profile with nil/empty members.
Construction migrates legacy reference audio and endpoint voice only to the currently configured
model/decoder or server/model when no profile exists. Existing profile values win; construction
otherwise retains the original global path/voice projection until a destination setter restores
its profile. A new destination gets no other destination's reference audio, voice or sampler.
SetLocalAssets changes the model and decoder together before resolving reference audio, so it
never creates a profile for an intermediate pair. Previous profiles remain available on return.
The unused generic reset API is not exposed as a new speech-reset action.

Playback invalidation is separate from ordinary presentation notification. Provider, path, voice
and rate mutations invalidate before changing the value; no-op comparisons use Swift canonical
string equality, trimming or rate clamping as appropriate. Each changed member of a paired edit
retains the original individual invalidation order. Per-language voice IDs remain distinct for
Simplified and Traditional Chinese. Rate stays within 0.5...1.5; nonfinite input resets to 1.0.
Stored NSNumber booleans are also Double-compatible: false resolves to 0, then clamps to 0.5.
Sampler edits apply only to local speech and reject a stale expected identity. They normalize the
request controls and persist before notifying presentation, without invalidating active playback.
INotifyPropertyChanged is the Windows presentation adapter; its notification count is not asserted
to match Combine's objectWillChange/@Published delivery count.

`Core/LanguageLearning/ModelDestinationIdentity.cs` adapts the original identity function without
using System.Uri as a formatter. It retains the exact four-string JSON key, including forward-slash
escaping; lowercases scheme/host, removes parseable user/password and default ports, trims trailing
path slashes and maps a nonempty empty-path destination to /v1. It preserves dot segments and
query/fragment spellings. Invalid parsed components retain the original candidate; an invalid
UTF-8 decoded path becomes empty as observed in URLComponents. This is a preference identity,
not endpoint admission: the existing endpoint configuration still independently rejects credentials,
queries/fragments and insecure public HTTP. Identity keys do not include the separately stored API
key. Raw address/query text is retained just as in the source; do not log an identity as a secret-free
URL. The map compares keys canonically while preserving emitted spelling.

`Core/Speech/SpeechGenerationProfile.cs` supplies immutable sampler/reference/voice values with
canonical optional-string equality/hash. `SpeechProfileCodec` preserves the original Codable
whole-map contract: missing required sampler/seed fields, invalid known values or one bad profile
discard the entire map; unknown fields are ignored. Exact decimal/exponent integer spellings are
accepted. Duplicate fields/destinations retain the source-observed first value. UTF-16/32 input
uses the shared Foundation encoding adapter. Codable lazily inspects known UTF-8 strings, so an
invalid byte in an ignored value can remain admissible, whereas the same byte in a reference path
fails decoding. The raw observation uses JSONSerialization separately and can expose that retained
blob as base64 when JSONSerialization rejects it. Encoding commits only after the complete map
succeeds. Durable top-k now retains Swift's 64-bit Int range in the shared sampling value; the
resolved native sampler narrows only after normalization bounds it to 1...256.

`Inference/Speech/SpeechAssetFiles.cs` is the filesystem boundary. Settings validate nonempty model
and decoder paths, GGUF extensions, distinct standardized URLs, readability and exact known pair
metadata. Reference WAV/MP3 is optional and must be readable, positive and at most 10 MiB. The
inspector uses Windows-compatible path/access semantics and observes metadata without loading
weights/audio. These observations do not pin file identity or bytes; a provider must still handle
replacement/access/decoder failure when opening the frozen configuration. Actual permission,
filesystem and media-device behavior remain platform gates. Endpoint model and voice retain the
256-byte UTF-8 limits and borrow the existing endpoint validator; neither configuration accessor
reads credentials or contacts a service.

The native verifier compiles all four complete original boundaries, original language/sampler/
endpoint/pairing values and the actual Keychain class declaration. Every settings construction
explicitly injects a spy around the original in-memory credential store; Keychain methods are never
called. Each row owns an isolated UserDefaults suite and private temporary sparse test assets.
There is no model execution. The 144 observations contain 104 full settings traces and 40 exact
identity outputs, with ten adaptations of the original LearningSpeechSettingsTests,
SpeechGenerationProfileTests and equivalent-endpoint ModelContextBudget test; 134 rows extend
source coverage. They do not execute the original Swift Testing suites.

Settings traces compare every public setting, selected profile and identity fields, complete raw
preference projection, explicit key-effect counts/results and every pre-mutation playback snapshot.
Temporary asset roots/Windows separators are normalized only for those asset paths. Profile Data
is projected through decoded JSON when possible, otherwise exact base64; identity/profile spelling,
integer values, user text, failures and no-op/restart behavior remain observable. Exact identity
cases additionally compare the emitted JSON string byte-for-byte. The C# runner uses the production
owner, codec and file inspector over its own temporary assets. Three extra cases check canonical
profile equality/frozen voice snapshots, general reset/secret exclusion and durable sampler
publication. No installed inventory, speech playback/provider/controller, settings UI, actual Anki
or Windows user-desktop parity is established by these component checks.

Validation passes: 144 source observations (also independently verified), 147 focused cases,
all 4,420 Core and 905 App tests, and the Release solution cross-build with zero warnings/errors.
Both final-head CI jobs (61 source verifiers) remain required; Windows execution stays separate. No installer, package, model download, artifact upload or new
transmission is introduced.
