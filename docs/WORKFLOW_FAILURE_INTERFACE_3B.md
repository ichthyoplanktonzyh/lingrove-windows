# Fixed workflow failures in the interface language

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, committed
`Lingrove/App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift`,
`ExpressionAssistCoordinator.swift`, `Lingrove/Services/Speech/LearningSpeechController.swift`,
`Lingrove/Models/Vocabulary/VocabularyEntryInput.swift`, `VocabularyExportModels.swift` and
`Lingrove/Services/Vocabulary/VocabularyExportSession.swift`. The Mac presents localized fixed
failures while retaining external diagnostics and literal task values. Original portable Core
messages, rules and fixture references remain unchanged.

## Connected paths

- Writing capture reasons, shared pause/off state, disabled translation and the three guarded
  replacement outcomes now translate at their fixed producers. The replacement authorization,
  acknowledgement and Copy recovery decisions are unchanged.
- Reading pointer/selection/region failure, OCR availability instructions, destination setup and
  same-target-language messages translate before entering the presentation. No change to OCR,
  host admission, request destination, capture freshness or task cancellation is made.
- Expression model/retry/insertion failure and unavailable favorite capture translate at the
  literal callsites. Unsupported-trigger guidance now recommends the existing shortcut without
  the obsolete ASCII-only claim; Unicode trigger admission is unchanged. Candidate, query,
  clarification and original editor text stay literal.
- Speech's three fixed voice/text messages use its existing injected template adapter. An optional
  typed-failure formatter is installed only by the production SpeechFeature; default portable
  controller behavior remains English. PCM, SAPI and encoded-playback failure events format at
  their owning producers; original parameterless Windows constructors retain English defaults.
  The controller keeps arbitrary event payloads literal. No audio/device work starts on attachment.
- Current notebook load/actions, semantic/category/edit forms, backup and export now adapt known
  exception categories before flattening them for display. The writing favorite bridge receives
  an optional enum formatter from its one application owner. It retains its original unknown-error
  fallback and does not acquire a second Store or backend.
- Word-list import/create uses all thirteen original Mac error cases, five table-specific `%lld`
  line templates and the existing Windows fallback. Contextual explanation errors and adoption
  failure now use the same presentation boundary; authored meaning and generated prose stay raw.
- The shared floating/notebook dictionary renderer translates fixed error/empty/loading states,
  rich-display fallback and explicit online/browser disclosure. Its closed dictionary failure
  vocabulary retains the Windows bundled-only contract without promising an Apple dictionary.
  The destination URL is an unchanged format argument; definition, source, license and query data
  remain literal, and the generic text/link factories still do no automatic lookup.

`InterfaceFailureText` reconstructs trusted source keys from vocabulary, export, translation,
explanation and speech failure enums. It never looks up a foreign exception's Message. HTTP status
is inserted only after selecting the fixed template. Unknown exception types return their original
message. The two Core translation/explanation state fields already admit only fixed policy messages;
their dedicated adapters accept the closed producer vocabulary and retain unknown values. These
adapters are not applied to source/result/clarification text or general diagnostics.

`InterfaceText.FormatFrom` provides table-specific formatting without changing existing callers.
The original Mac catalogs are byte-identical. All 463 prior Windows supplement entries retain
their values. Fifty-five Windows-specific fixed messages add authored translations in every one
of the fifteen supported interface locales, bringing the supplement to **518** keys.

## Evidence and remaining scope

The Release solution built with zero warnings/errors. Three new dispatcher tests passed after
correcting a missing System.IO test import: all fourteen non-English locales resolve the owned
failure categories; arbitrary diagnostics that equal a catalog key remain literal; a misleading
derived vocabulary exception cannot replace its enum-owned source key; HTTP status and imported
line numbers retain their values; and an actual preview keeps source text literal while displaying
the Chinese fixed failure. The test's owned render is available through `LINGROVE_TEST_RENDER_DIR`.
Resource verification preserved all prior values and the three exact Mac catalog files.

Independent review found the additional PCM event and shared dictionary paths; the final resources
and producer callbacks resolve those findings. Follow-up regression adds actual dictionary rendering,
SAPI/encoded fixed-event formatting through substituted effects without devices, and PCM invalidation
formatter assertions. Complete WPF regression, portable App boundary regression and final-head CI
outcomes are recorded in the PR. No user editor, clipboard, screenshot, notebook, model, endpoint
or audio device is used. This is a presentation change, not native desktop or linguistic-quality
acceptance. Real Word/Chrome/Codex, RTL/mixed-DPI, screen-reader and physical interaction checks
remain the maintainer's final round.

This module does not claim that every external/native diagnostic has a translation. Raw provider,
OS, path, model and user values remain literal. Additional fixed review/controller messages retain
their own finite inventory; physical language-confidence, Apple-only dictionary and media gaps
are not closed by localization.

The final focused WPF run passed **12/12**, zero failures/skips, including all five new interface
cases and the existing Windows speech-provider cases. The injected SAPI/decoder failures kept
their original tokens and never opened native voices or devices; both produced the expected
Chinese fixed event text rather than the substituted private diagnostic. The PCM callback test
also checks its enum-owned message before the existing joined shutdown assertions.
