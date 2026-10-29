# Local language detection: Windows API audit

This audit reads the committed Mac target `3b53402f2cbc196b0398d133177fd80006d165b2`
through Git. It does not upgrade the existing fixture reference or activate a detector. The
Windows multi-source translation gap remains open.

## Separate Mac responsibilities

- `Lingrove/Models/LanguageLearning/LearningLanguage.swift`, `detect(in:)`, feeds the last
  600 Swift characters (extended grapheme clusters) to a fresh, unconstrained
  `NLLanguageRecognizer`, sorts at most two hypotheses, and requires a top score of at least
  0.55 and a lead of at least 0.15 when a runner-up exists. It maps the two Chinese variants
  separately and returns nil for unsupported winning codes. The associated
  `LingroveTests/Models/LanguageLearning/LearningLanguageTests.swift` checks identifiers and
  Chinese locale mapping, not a native detector corpus.
- `Lingrove/Support/LanguageLearning/LearningSourceLanguageResolver.swift` and
  `LingroveTests/Support/LanguageLearning/LearningSourceLanguageResolverTests.swift` define a
  different, constrained recognition path for explicit translation. They filter duplicate and
  target languages, use a single remaining source directly, otherwise constrain the recognizer to
  enabled sources, fill absent candidates with zero and apply the same thresholds. Windows already
  has the pure rule and injectable boundary in `Core/LanguageLearning/LearningSourceLanguageResolver.cs`
  and `ILearningLanguageDetector.cs`; the production scorer is unavailable. Its current missing
  provider behavior is documented in [the preview record](LEARNING_PREVIEW.md).
- Speech has its own phrase/context evidence, 0.75 floor, 0.20 margin and hint fallback; see
  `Core/Speech/SpeechLanguageResolver.cs` and [its record](SPEECH_LANGUAGE_RESOLUTION.md).
  A future recognizer must retain unsupported codes for this path rather than projecting every
  result into the 14-language translation palette.

## Evaluated Windows interfaces

Microsoft's [ELS language detection contract](https://learn.microsoft.com/en-us/windows/win32/intl/microsoft-language-detection)
returns language names ordered by relevance, including `zh-Hans` and `zh-Hant` where applicable.
The [recognition example](https://learn.microsoft.com/en-us/windows/win32/intl/requesting-text-recognition)
describes those names as sorted by confidence, but supplies no numerical confidences or supported
candidate-constraint contract. Therefore its output cannot establish either threshold or reproduce
constrained candidate probabilities. A rank, script, keyboard locale or fixed score is not evidence
of the required probability. ELS is installed with Windows 7 and later according to
[Microsoft](https://learn.microsoft.com/en-us/windows/win32/intl/about-extended-linguistic-services),
but availability alone does not make it a compatible scorer.

The documented [IMultiLanguage2 interface](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa741001(v=vs.85))
offers `DetectInputCodepage`, which detects an encoding, not language hypotheses. Its code-page
results cannot be used as language confidence.

The [Microsoft Edge Language Detector API](https://learn.microsoft.com/en-us/microsoft-edge/web-platform/languagedetector-api)
is an on-device browser API that reports numeric language confidences, but Microsoft documents an
initial model download and possible additional resource downloads for expected languages. The
document describes `expectedInputLanguages` as a resource/accuracy hint, not a constraint on the
candidate set. WebView2 availability, offline preinstalled resources, all required language and
Chinese-variant coverage, and equivalence of its scores to the Mac constrained contract have not
been established. It is not a verified native Windows adapter under this campaign's no-download
scope. Azure language detection is a network service, outside the local-only request.

## Handoff and verification

No native adapter or production hookup is introduced by this audit. Multi-source translation must
continue to fail closed; an explicitly selected single source remains available under the existing
resolver. No source text, clipboard, user files, model weights or external endpoint were accessed.
The Mac source and its cited tests and Microsoft's published contracts were inspected; no detector
was run and no native accuracy, physical desktop, or visual acceptance is claimed. A future provider
needs documented genuine numeric hypotheses, constrained source behavior, unsupported-language and
Chinese-variant handling, local availability and privacy, followed by synthetic native and policy
tests before production integration.
