# Home and welcome product examples

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, under `Lingrove/`:

- `UI/Onboarding/HomeLearningShowcase.swift`, `HomeLearningDemoModel.swift`,
  `HomeLearningDemoFixtures.swift` and `ProductFeatureExample.swift`;
- `UI/Onboarding/LearningFeatureShowcase.swift` (shared area navigation; its older decorative
  illustration is not the current welcome learning sheet);
- `UI/Onboarding/Welcome/WelcomeView.swift` and `UI/Settings/Panes/HomePaneView.swift`;
- the sentence/dictionary cards, expression candidate view and notebook/review components reused
  by those callers; `LingroveTests/UI/Onboarding/HomeLearningDemoTests.swift`.

`HomePane` now mounts `ProductFeatureExamples`: Autocomplete and Language Learning share one area
switch. Welcome exposes two explicit View example buttons. Each opens the same player, with
learning starting at Writing and hover autoplay disabled for the explicit example window. This
replaces the old static welcome translation text. These are product examples, not real editor
captures or evidence that an illustrated Windows acquisition/replacement is supported everywhere.

## Complete mounted flow

`LearningShowcaseCatalog` preserves seven scenes and their source step order: Writing (9),
Expression assistance (4), Reading (7), Dictionary (6), Read Aloud (3), Vocabulary (5), Anki (4).
The player provides manual steps, Previous/Next, Play/Pause, Replay, hover-scoped playback and a
reduced-motion resting state. Actual interaction revokes scripted action epochs. Leaving a scene
retires its transient dictionary/explanation/speech state; unmount drains the mounted sample owners.
Existing interface-language keys localize scene/action copy. Missing translations retain the
existing English fallback; this module does not establish full application string coverage.

The player embeds the real sentence/dictionary cards and expression candidate view. Writing uses
the source's bounded Chinese/English material, expands/collapses, explains, saves/removes, exposes
the saved menu, replaces only the sample sentence and dismisses. Reading demonstrates selection,
pointer and screen-region material within the example, using the current configured shortcut
labels without registering another hook or capturing a screen. Dictionary word/page/source changes
use the existing session with fixed local answers. The sample attribution states that no dictionary
service is contacted; its System source is an illustration, not an activated Windows system dictionary.

`LearningShowcaseStore` owns one isolated, in-memory canonical v8 backend plus its normal writing
view adapter. The same owner serves sample favorites, notebook editing/graph and review cards.
Stable source/example/sense/card IDs and acknowledged Store transactions remain in force. No
production notebook, settings persistence, legacy archive conversion or cloud service is borrowed.
Graph/list labels use the production notebook's sense-title rule, including encountered meanings.
Saved-menu navigation opens the real notebook at that sample selection. The notebook borrows the
sample explanation/dictionary/review owners, not AppHost's providers.

Anki embeds the existing canonical preparation/layout editor and session. It prepares the same
phrase capture used by writing/dictionary, resolves the acknowledged destination, edits Front/Back,
sends and verifies a note receipt, and navigates the sample deck. Its stateful in-memory Anki
adapter never contacts localhost or activates Anki. The header labels the sample destination.
Notebook import/export/backup/restore, clipboard/media transfer and entry/review file-image input
are disabled in the example; ordinary production callers retain their defaults.

Speech has an independent system-only controller. Constructing or stepping the example does not
play sound, enumerate voices, load a model or contact an endpoint. Only an explicit speaker/audio
action uses the existing Windows system provider/exporter. Tests inject silent speech, do not play
audio or synthesize a native recording, and do not establish hardware/voice acceptance.

## Windows presentation and lifetime

`ProductFeatureExamples`, `LearningShowcasePlayer` and the small example adapters own UI only.
`FrozenVocabularyStoreFactory.CreateInMemory` reuses the existing canonical SQLite owner. Embedded
card modes neither show nor activate hidden host windows, preserve the normal production card
behavior and disable clipboard actions. The review editor carries its scoped styles with it when
embedded, shows its preview first and retains acknowledged Save/Send through the actual session.
WPF uses the shared light/dark palette, Segoe UI, wrapping controls and a bounded 330-DIP scene
viewport. A separate 680-DIP example window adapts the Mac sheet, with 520-DIP minimum width and
scrolling. Pixel equality is not a requirement.

Close waits for the player before completing window retirement. AppHost drains remaining mounted
players before its production owners and retains all existing cleanup finally blocks. Sample
notebook/review windows, graph workers, sessions, accepted Store work, system speech and the SQLite
backend retire in dependency order; one cleanup failure does not skip the other owners. WPF/native
resource owners remain separate from production input/focus/model services.

## Validation boundaries

The primary implemented, reviewed and validated this complete flow alone after the maintainer
ended delegation. Previously interrupted drafts were preserved and completed; they are not treated
as finished modules or independent review of this implementation.

Focused compiled WPF checks exercised every step in all seven scenes in light and dark, real note
receipts, both welcome buttons/window closes, dictionary/editor reuse, private Store isolation,
playback/reduced-motion state and retirement. The expanded scene/review/dictionary group passed
93/93 before the final file-dialog guards. An earlier integrated Release build passed with zero
warnings/errors and the entire WPF suite passed 1,058/1,058 (3m46s), with no skips; final-source
full-suite results and final-head CI are recorded separately when complete.

Rendered all seven real scene controls in light/dark at 96-DPI output. Inspected source/result and
sample-destination disclosure, dictionary definitions, notebook graph labels and review preview
hierarchy. No corresponding committed Mac screenshot was available; IA comparison is source-based.
Physical mouse/keyboard, reduced-motion setting changes while mounted, mixed DPI, screen readers,
actual SAPI playback/export, and final Word/Chrome/Codex acceptance remain unverified. These checks
use synthetic/sample data, fake Anki and silent speech, with no user editor, clipboard, screenshot,
model, live HTTP request or production notebook. No installer, model download or release is created.

Final production code `e6363631b4e1e1d209df6bc5c8c4d02819935870` compiled in Release with zero
warnings/errors. With `LINGROVE_TEST_SYSTEM_SPEECH=0` and `COTABBY_CLIPBOARD_TEST=0`, the complete
suites ran sequentially and passed without skips: Core 5,079; App 1,278; Inference 750; WPF 1,058
(3m49s). Commands were `dotnet build Cotabby.sln --configuration Release --no-restore`,
`dotnet test tests/<suite>/<suite>.csproj --configuration Release --no-build --no-restore` for
Core/App/Inference, and `./scripts/test-wpf-dev.ps1 -NoBuild -NoRestore` for the desktop lease.
The rendering harness supplies the window background to the captured client control so transparent
areas retain the actual light/dark backdrop; this is component rendering, not desktop capture.
Final review also found that collapsing a retained Home pane does not necessarily raise WPF
Unloaded. Visibility retirement now disposes the sample scope as well as stopping autoplay, and
showing it again mounts only after retirement completes. The seven-scene/window group passed 3/3
again with explicit hide/remount and both welcome-close paths. This follow-up changes no model,
notebook or transport policy; the final PR's four required CI jobs still gate merge.
