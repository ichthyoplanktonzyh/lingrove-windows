# Active workflow interface copy and localized state updates

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, read from committed Git objects.
This extends [the learning-state checkpoint](LEARNING_INTERFACE_STATES_3B.md). It is another
connected interface pass, not a declaration of complete internationalization or native acceptance.

## Source and responsibility mapping

- `Lingrove/UI/Components/CompletionPreviewText.swift` and
  `LingroveTests/UI/Components/CompletionPreviewTextTests.swift` map to Core's
  `Settings/CompletionPreviewText.cs`, its tests, Appearance preview and Welcome demo. The exact
  original spacing, punctuation and malformed-marker cases are retained. A sentence is translated
  as one unit with one movable `¦`; zero/multiple markers become readable typed text with no ghost.
- `Lingrove/UI/InlineFeatures/LearningSentenceCard.swift`, `LearningTranslationCard.swift`,
  `LearningDictionaryCard.swift` and `LearningExplanationView.swift` remain the card/state reference.
  WPF preview/contextual partials translate fixed updates after initialization, retain exact captured
  content, and localize Copy feedback without changing its receipt/current-document checks.
- `Lingrove/UI/Onboarding/Welcome/WelcomeView.swift`, `HomeLearningShowcase.swift`, the committed
  `Localizable/Learning/Vocabulary.xcstrings` and existing recommended-setup owner define onboarding
  copy and examples. The App setup controller exposes a formatter for its finite stage keys and
  numeric progress. The default remains portable English; the WPF borrower supplies the frozen
  interface formatter. Model identity, transfers, cancellation, retry and download authorization
  remain with the existing owner. Speech is configured separately, replacing an obsolete claim
  that Windows speech is wholly unavailable.
- `Lingrove/UI/Settings/Panes/DictionarySettingsSection.swift` and
  `Engine/SpeechConfigurationView{,+Local,+Endpoint}.swift` map to the existing WPF dialogs.
  Fixed actions, provider/seed labels, filters, receipts, confirmations and privacy guidance are
  localized. Enum values, source/website names, file patterns, filenames, voices, model IDs and
  endpoint arguments retain their original identity. Opening these views does not grant inference,
  remote lookup or model-download permission.
- `Lingrove/UI/Vocabulary/VocabularySemanticPresentation.swift` and original Vocabulary resources
  supply semantic relation/axis/formality/medium labels. Only finite notebook actions enter a
  lookup; generic button helpers still accept literal user category/meaning titles. Category
  catalog guidance follows interface locale with the catalog's original language fallback. User
  overlays, study-language settings, query strings and record identities are unchanged. Current
  notebook/import/graph/image controls and batch review borrow the existing Store/session owners.

## Resources and WPF adaptation

The supplement grows from 244 to 382 keys, all in 15 interface locales. The original 244 entries
and all three copied Mac catalog files are unchanged. New finite adapters name their exact source
keys and reference; Windows-specific instructions/errors are authored in the supplement. Reused
short captions may compose original localized labels with a separator; generated or authored study
content never enters this composition. The language autonyms remain the existing native-script
choices. License names, CPU, file extensions and unit/number-only format syntax are technical tokens.

The writing card's heading, explanation controls, expand/replace names and Copy feedback now remain
localized after refresh or compact/expanded transitions. Import summaries use the explicit Vocabulary
table, and columns/counts are formatted before literal imported content is inserted. The current
semantic relation switch uses `More specific than` for Narrower, preserving the Mac wording.
Seed display explicitly uses the Learning table even when another table has a `Recommended` key.

The new Arabic card direction affects chrome; source/result bodies retain their study-language
direction. Endpoint/model disclosures, speech endpoint inputs and the website URL template explicitly
retain LTR direction. Dictionary dialogs use the frozen interface direction. Rendering remains WPF,
Segoe UI, themed spacing and existing sizing/keyboard behavior. No physical screen coordinates,
capture bounds, insertion target, graph IDs or data/storage/binary/IPC identifiers are translated.

## Executed checks and remaining scope

- Release solution build: zero warnings/errors.
- Core tests: 5157 passed, zero failed/skipped; portable App tests: 1278 passed, zero failed/skipped.
- Focused WPF suite through `scripts/test-wpf-dev.ps1`: 53 passed, zero failed/skipped.
  Owned Chinese/Arabic cards and Chinese import previews exercise live transitions, literal brace/
  brand-looking values, explicit destination direction, column labels and receipt counts.
- Catalog checks preserve all old entries/bytes, all 15 locales, positional .NET and Mac printf
  slots, and successful formatting in each locale. They also check the original preview marker
  boundary in every interface language. Full independent regression and final-head CI belong to
  the PR record.

These tests use owned synthetic views/data, fake providers and in-memory/owned stores. They do not
read a user's editor, clipboard, screen, notebook, endpoint or model, or activate an audio device.
Optional renders capture only these owned visual trees in light/dark/simulated high contrast at
96 DPI; they are component evidence, not physical desktop, mixed-DPI or screen-reader acceptance.
The owned Chinese import/dark and Arabic card/light renders were visually inspected. The Arabic
card render uses its actual SizeToContent dimensions and checks parent bounds before capture;
forcing a wider synthetic root would misrepresent the inherited RTL transform. No current Mac
desktop screenshots were available in this pass; hierarchy/state comparison uses committed views.

The initial inventory found 60 settings metadata keys and 12 card headers lacking resources. This
pass handles some shared labels but leaves the remaining long descriptions/headers for the next
connected resource batch. Further auxiliary accessibility names, finite domain messages and some
onboarding XAML copy remain visible backlog; unknown OS/provider/remote diagnostics remain literal.
Translation quality, full RTL/window usability and Word → Chrome → Codex host acceptance stay with
the maintainer's consolidated final pass. Native language confidence and media/dictionary platform
limitations retain their separate handoffs.
