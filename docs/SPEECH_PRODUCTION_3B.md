# Speech production integration

This work targets macOS `3b53402f2cbc196b0398d133177fd80006d165b2`, including
`Lingrove/Services/Speech/LearningSpeechController.swift`, `SystemSpeechProvider.swift`,
`SpeechAudioPlayer.swift`, `SpeechAudioGenerator.swift`, `LocalSpeechRuntime.swift`,
`SpeechEndpointClient.swift`, `SystemSpeechAudioExporter.swift`, `Lingrove/UI/InlineFeatures/LearningSentenceCard.swift`,
`Lingrove/UI/Settings/Panes/Engine/SpeechConfigurationView.swift` and related local/endpoint
sections, plus `LingroveTests/Services/Speech/LearningSpeechControllerTests.swift`,
`GeneratedSpeechControllerTests.swift` and `SystemSpeechProviderIntegrationTests.swift`.
The speech service and policy sources are unchanged between the previous 35b pin and 3b.

The new Windows feature composes the existing speech controller, independent helper/endpoint,
PCM queue and renderer once on the UI dispatcher. SAPI voice enumeration occurs only on an
explicit settings or playback action. SAPI receives text with XML parsing disabled; each worker
has an operation token and is joined during shutdown. Encoded WAV/MP3 playback decodes through
the existing bounded Media Foundation codec, retains stereo through tempo processing and uses
an explicit WASAPI player. Voice metadata has no equivalent quality ranking in this adapter, so
available voices use the standard rank rather than invented quality information. A separate
speech folder uses the existing GGUF staged importer and protected no-follow deletion, without
creating or using the completion/translation model runtime.
The native helper is built separately from the ordinary .NET application build. The explicit
[Windows development build](WINDOWS_DEV_BUILD_3B.md) can now compile, check and embed it at the
existing runtime path with `-IncludeLocalSpeech`; managed-only builds still report unavailability
when no helper has been prepared. No model or prebuilt helper is downloaded by this work.

Windows Extended Linguistic Services returns a relevance-ordered language list, not the
confidence values required by the source resolver. The current integration passes no guessed
scores; automatic requests retain the source resolver's card-language fallback. An unknown source
language cannot start speech until a trustworthy recognition boundary is available. The settings
audition uses an explicit fixed language. This is a platform gap, not full automatic detection.

System recording uses SAPI `ISpStream` over a COM-visible bounded stream. Writes are rejected
before exceeding 90 seconds of 24 kHz PCM16; cancellation, a 120-second deadline and overflow
retire the work before its joined MTA returns. No audio device or temporary recording file is
opened. An explicit synthetic English fixture generated and decoded a WAV using an installed
voice on this Windows host; it did not play sound or read user text. CI without installed voices
cannot establish this native path. SAPI native calls themselves are not forcibly interruptible;
a nonresponsive engine can delay cancellation and shutdown despite the deadline.

AppHost now owns one SpeechFeature, with an independent DPAPI credential file in the Cotabby
local data root. The existing Review Feature borrows its controller and the canonical Store;
writing windows and every new reading presentation receive that same controller. Learning settings
open the single speech configuration window. Normal shutdown drains cards/notebook/Review before
closing speech configuration and releasing providers, then releases the canonical Store. Attachment
does not enumerate voices, open audio devices, launch a helper or read speech credentials.
Native audio hardware, real clipboard/editor
focus, endpoint operation, helper generation, screen readers and real high contrast remain
unverified. Tests use isolated temporary GGUF files, in-memory audio and substituted provider
effects, with no real audio playback, model download or network endpoint.

`ISpeechDestinationDisclosure` publishes the validated endpoint origin and bounded model label
without reading credentials or exposing URL userinfo/query. It reports configuration availability
and updates through the controller's UI-context notifications. Writing and settings display this
same description; review preparation requires a current disclosure before generating audio.

The reading card borrows this owner through `ReadingPresentation.AttachSpeech`. A click waits
until the destination is rendered, then rechecks the captured reading selection, application
admission and speech configuration identity before dispatch. Hiding, switching to the dictionary
or replacing the text retires the relevant target. The application must attach the same owner
to each reading presentation; the application now makes that attachment when it creates the session.
Reading carries an in-memory synchronous admission callback through the shared controller and
generator. The callback checks the captured selection authority, presentation/settings epochs
and cancellation without touching WPF or credentials. The SAPI worker rechecks immediately
before synthesis, the local helper rechecks before launch and before writing text to its pipe,
and endpoint transport checks immediately before `SendAsync`. A missing permit leaves ordinary
speech behavior unchanged; a provider that cannot honor a non-null permit fails closed.
System synthesis is serialized: one real worker drains before the latest pending request starts,
and intermediate requests are discarded. Shutdown joins the worker even if native code is slow.
