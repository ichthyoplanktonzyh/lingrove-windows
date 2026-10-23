# Speech provider metadata and voice-language matching

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Support/Speech/SpeechModelPairingPolicy.swift`, and `LocalSpeechLanguage` plus
`SpeechVoicePolicy.languageMatch` in `Cotabby/Support/Speech/SpeechTextPolicy.swift`.
This is a portable metadata boundary; it does not select or invoke a provider.

`SpeechModelPairingPolicy` retains the exact two Qwen3-TTS model filenames, three matching
decoder aliases and display labels. It publishes immutable ordered companion lists. Known
cross-package combinations are mismatches; unknown custom files remain the later runtime's
responsibility. There is no trimming, path probing, case folding or guessed file compatibility.
`LocalSpeechLanguage` preserves the original fixed inventory, including both Chinese choices
mapping to `zh` and unsupported palette languages returning null.

`SpeechVoicePolicy.MatchLanguage` takes a voice tag and the selected learning language and
returns exact/same-language compatibility or null. It preserves Foundation-style trimming,
underscore normalization and lowercasing. Chinese requires a matching explicit Hans/Hant script
or a script inferred from the first two-grapheme region component. An unknown first region does
not permit scanning later regions; a bare `zh` does not match either Chinese choice. These pure
rules belong in Core so a later catalog adapter can use them without owning platform services.
The partial policy type does not yet implement catalog filtering, ranking or voice selection.

`verify-speech-metadata-macos-parity.py` compiles the complete original pairing policy,
LocalSpeechLanguage and SpeechVoicePolicy, actual SpeechVoiceQuality/SpeechVoice declarations,
and complete original language/catalog declarations. Its driver invokes only the four pairing
methods, local code lookup and languageMatch. It executes no voice selection, localized ordering,
recognizer, file access or provider. The 196 original-source observations cover exact/custom
filenames, all fourteen palette languages, normalization, Chinese contradictory scripts/regions,
Unicode graphemes and unusual tags. Expectations were captured from that executable; both
runners lock the reviewed IDs. These are source-derived policy extensions, not execution of
the original Swift Testing suite.

All 196 source observations passed locally and independently. Core runs the same cases plus
one mutation-resistance check for shared companion lists: all197 focused cases and the full
Core4109 suite passed with zero failures/skips. Release solution cross-build passed with zero
warnings/errors. Independent production/evidence reviews are clear; both final-head CI jobs
remain required before merge.

Installed voice inventory, localized identifier ordering, voice selection, language recognition,
speech settings, synthesis, playback, device callbacks and review-audio integration remain work.
This slice adds no AppHost/UI wiring, files, endpoint requests, model downloads, installers,
releases or uploaded artifacts. The existing PCM/framing/export foundations retain their own
evidence and native verification gaps.
