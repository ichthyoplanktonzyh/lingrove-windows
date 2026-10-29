# Speech production integration work in progress

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
The native helper is built separately and is not included by the existing application build;
local playback therefore reports helper unavailability until an independently built helper is
placed at the runtime's configured path. No model or helper is downloaded by this work.

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

The application owner and card/settings presentation are not yet attached to AppHost. Native
audio hardware, real clipboard/editor
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
to each reading presentation; this component alone does not activate that composition.
