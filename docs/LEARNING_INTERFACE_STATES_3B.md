# Active learning interface states and destination copy

The subsequent [active workflow copy pass](ACTIVE_WORKFLOW_INTERFACE_3B.md) extends this historical
244-key checkpoint to 382 supplement keys and fixes further dynamic/auxiliary paths. Counts and
remaining-scope notes below describe this earlier module, not the full current interface inventory.

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, read from committed Git objects.
This module follows `Lingrove/UI/InlineFeatures/LearningSentenceCard.swift`,
`Lingrove/UI/Settings/Panes/ExpressionAssistSettingsSection.swift`,
`Lingrove/UI/Settings/Panes/Engine/LearningModelConfigurationView.swift`,
`SpeechConfigurationView+Endpoint.swift`, `Lingrove/Models/Runtime/OpenAICompatibleEndpointModels.swift`
and the original Localizable/Learning/Vocabulary tables. It extends the
[initial language coverage](INTERFACE_LANGUAGE_COVERAGE_3B.md), not the campaign's scope or pin.

## Connected behavior

The current writing card now localizes its live loading/ready/failure labels, provisional result
heading and Cancel/Close action every time state changes. Initial-tree translation no longer gets
overwritten by English updates. Capture failures keep their diagnostic text as content. The
dictionary-form line formats the original Mac parameterized resource before inserting its exact
query. Fixed uppercase XAML keys have explicit resource adapters. Speech action accessibility names
use the original Play/Stop source/translation meanings; shared favorite controls localize their
accessible name and loading/unavailable/pending/saved status without changing their Store lease.

`LearningModelDestinationText` keeps model/path sanitization and validated endpoint rendering while
localizing fixed templates. Translation settings disclose the normalized address, exact safe model,
payload, validated host scope, opt-in state and draft-reference state. Explanation settings borrow
the same presentation helper used by cards. A settings-only empty-model inspection flag may show a
valid address before choosing a model; all normal consumers still require a model. It grants no
generation permission and does not change endpoint selection, credentials or transmission.

`EndpointInterfaceText` maps only the existing typed configuration errors and host scopes. The
Inference model and its portable English diagnostics are unchanged. Unknown remote/provider errors
remain diagnostic content. URLs, model identifiers, user text, query arguments and filenames never
become interface lookup keys. Completion's LAN/public warnings retain the source's privacy meaning
with explicitly adapted Windows platform wording.

The portable speech controller borrows a frozen-locale template formatter from `SpeechFeature`.
Its default formatter remains ordinary .NET for portable tests. Only fixed destination templates
and fixed redaction labels enter this adapter; endpoint origin, sanitized model and voice are
arguments. Speech controller/provider/session ownership, credential timing, request admission,
cancellation and shutdown remain unchanged. Formatting a destination activates no provider.

Reading settings now describe pointer reading and explicit-region OCR without the retired editable
field prerequisite. The obsolete ASCII-only warning is removed: the current deterministic trigger
policy admits Unicode symbols while native insertion still checks exact current field/range evidence.
The real multi-source confidence limitation stays visible, localized, and fail-closed. Reading
provider/dictionary and local Windows OCR guidance are separate readable notes.

## Resource and Windows adaptation

The three copied Mac catalogs remain byte-for-byte unchanged. The Windows supplement grows from
191 to 244 keys in all 15 supported locales: 37 finite source-key/composition/platform adapters reuse
committed Mac translations; 16 Windows-specific state/disclosure templates are authored here.
Existing supplement translations are preserved. Each reuse names the original table/key and pin
in its resource comment. Compositions are finite authored task labels, not runtime fuzzy/case lookup.
Platform substitutions change the proper platform name Mac to PC only in the two host warnings.

WPF retains its themed cards, Segoe UI controls, bounded scrolling and existing source/result RTL
rules. User/provider text remains literal even when it resembles a resource key or contains format
braces. No data compatibility, storage identifier, binary name, IPC or brand-attribution changes
belong to this module. Translation quality and physical screen-reader/RTL/mixed-DPI acceptance
remain part of the maintainer's final unified pass.

## Validation and remaining scope

Release solution build passed with zero warnings/errors. The focused active learning/interface/
favorites/reading/speech suite passed 82/82 with no skips. New owned fixtures exercise Chinese live
state changes, exact model/query arguments containing braces and Cotabby text, rejected secret URL
components, typed privacy/error labels, settings-only empty-model inspection, and the actual speech
controller formatter without provider activation. Full independent regression and final-head CI
results are recorded in the PR. Tests use owned synthetic views/text/files and substituted effects;
they do not access a user editor, screen, clipboard, notebook, endpoint, model or audio device.

This is not complete internationalization. Other authored settings descriptions, onboarding and
showcase copy, auxiliary forms, speech-model management receipts/confirmations and additional typed
domain errors still need their current-path inventory and localization. Unknown diagnostics stay
raw. Native providers, statistical language confidence and media/dictionary platform gaps retain
their separate records. Word, Chrome and Codex acceptance remains consolidated at the end.
