# Compiled learning-view regression tests

`tests/Cotabby.App.Wpf.Tests` exercises the compiled production windows and their XAML on Windows
x64. It complements the portable App policy/ownership tests. The behavior reference remains Mac
revision `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

- `CotabbyTests/UI/InlineFeatures/LearningDictionaryCardTests.swift`: attributed literal definitions,
  bounded card body, component order/navigation and replacement of old content.
- `Cotabby/UI/InlineFeatures/LearningExplanationView.swift`: original-range example highlighting,
  separate meaning/Details, refresh retention, changed-input collapse, one header action.
- `Cotabby/Services/Presentation/LearningSentencePanel.swift`: one reusable nonactivating window
  whose current document owns its commands and explanation.

The Windows adapter uses a collection-owned STA thread, dispatcher and **plain WPF Application**.
It never constructs the product `App` or `AppHost`. Each test creates/closes its windows on that
thread; the fixture shuts down the Application after the collection. The internal friend assembly
exposes existing view entry points only to these tests. Production views still receive frozen Core
snapshots and emit immutable intents; they own no provider, global listener or editor authority.

The tests load real resources, show the actual windows, run layout, render an in-memory bitmap and
inspect the native `WS_EX_NOACTIVATE`/`WS_EX_TOOLWINDOW` bits. They check literal Unicode/markup,
exact highlighted ranges, response/example direction, bounded long-text scrolling with attribution
outside the body, Details retention/reset, target/snapshot-bearing routed button events, provisional
versus completed content, stale explanation rejection and hide/reopen cleanup. Rendering checks for
a nonempty visual; it is not a screenshot comparison or a font/caret alignment measurement. Routed
button events check wiring and receipts; they do not simulate physical mouse coordinates.

This coverage also fixes two view-level omissions: sentence-card action buttons now explicitly
decline keyboard focus, and hiding a reused card clears its retained captured/translated/title text.
These source invariants and the native style bits alone do not prove focus retention in another app.

Run on Windows after the solution build:

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --no-build --no-restore --blame-hang-timeout 2m
```

The Windows CI job runs this suite; macOS cross-compiles it but cannot execute WPF. No clipboard
access, UI Automation, external editor, global input, model, endpoint, screenshot file, artifact
upload, installer or release is involved. Real editor activation, mouse hit testing, selection/copy,
IME, external-field freshness, mixed-monitor DPI, caret/ghost pixels, insertion acknowledgement and
CPU/GPU model execution retain their separate Windows-host gates in `PORTING_PLAN.md`. A green view
suite does not promote those parity rows to verified.

The [explicit Copy adaptation](LEARNING_COPY.md) adds footer/feedback and old-button identity cases
to this suite. Its Copy events have no native writer attached in tests, so CI still leaves the
system clipboard untouched. PR #21's initial 14-view suite passed on Windows x64 at
`ccaf8ddeda47e295f6ea9f88dfe35eafe92a71ae` (run `39882608095`). Later additions run through the
same CI entry point; that initial result only covers its recorded revision.

The independent [readonly text-selection adapter](LEARNING_TEXT_SELECTION.md) also runs here,
including actual TextPointer hit testing, popup styles, scrolled clipping and in-memory pixel
differences for selection paint. Card integration cases now verify immutable document/target events,
selection retention across refresh/annotations, attribution-safe feedback and page/target/hide
retirement. These cases leave physical pointer/native-host selection gates open.

The vocabulary favorite slice adds six archive-backed WPF tests (41 total compiled view cases).
They render the original fish geometry and verify acknowledgement, pending, exact Remove, fixed
errors, stable selection routing and retirement of reused sentence/dictionary bindings. SQLite
is isolated in memory. An async fixture helper pumps the same STA dispatcher while these real
archive tasks complete. [Favorite scope](VOCABULARY_FAVORITES.md) keeps notebook and native
physical-input verification gaps explicit.

The [notebook browser](VOCABULARY_NOTEBOOK.md) adds six archive-backed cases (47 total view cases):
activating window styles, literal long examples, filters/global selection, close/reopen, directed
cold load, merged aliases/deletion, latest-wins workers and draining, and the shared single-window
route. Its render check uses the actual wider notebook bounds. Native activation styles establish
the intended window type, not physical activation behavior in another app. No model or notebook
file is used by these view tests.

The [entry editor](VOCABULARY_EDITOR.md) adds seven cases (54 total view cases): isolated Cancel,
Save admission, original-text reset, acknowledged Save/pruning, conflict retention, concurrent
captures, text formatting/order, existing image-reference preservation, pending-save retirement
and the actual modal route from a filtered browser. A graph-only reference fixture does not prove
media bytes; a controlled TimeProvider blocks scheduling of a real archive operation. Views still
use actual Store/SQLite and no external editor, model or clipboard.

The [sense and usage actions](VOCABULARY_ACTIONS.md) add eight cases (62 total): actual New/Merge/
Move/Delete commands, owned confirmation Cancel, stable identities, empty-source retention,
retired menus, fixed failures/recovery, later browsing during a blocked mutation and draining an
admitted transaction during retirement. These cases keep native physical input and popup-focus
verification separate from routed events and actual archive acknowledgements.

The [notebook session library](VOCABULARY_NOTEBOOK_SESSION.md) adds 33 dispatcher cases (95 WPF
assembly cases total): 24 original Swift observations and nine transition/worker regressions. They
execute real App/Core sessions without constructing a notebook window. This count does not imply
that the notebook UI has been connected to explanations/dictionary, or that native input was tested.

[Notebook contextual explanations](VOCABULARY_EXPLANATION_UI.md) add eight actual window/archive
cases (103 total): explicit generation/stop, meaning precedence/proposal/adoption, retained Details
and selection, hide/reopen/reset, detached/removed targets, configuration and retirement during a
blocked durable adoption. Fake generators remain external boundaries; real model/native input is
not tested. Dictionary disclosure integration remains open.

[Notebook dictionary disclosure](VOCABULARY_DICTIONARY_UI.md) adds nine actual window/archive/
native-ICU cases (112 total), covering explicit query, word/Back navigation, shared literal attributed
definitions, retained expansion, form edits, collapse/reopen/late results, preparation failure/retry
and canceled pending queries during explicit Show or empty-form expression editing. Source fallback,
physical input and external editor behavior remain outside this evidence.

The [image preparation library](VOCABULARY_IMAGE_PREPARATION.md) adds 26 native WIC/PNG cases
(138 total WPF-assembly cases). These exercise an unconnected decoder/preparer, not user-facing
image import or notebook media writes. Ten cases share actual Mac ImageIO observations; platform
orientation/profile/multiframe and output-byte-ceiling cases have separate evidence scope.

The [media storage library](VOCABULARY_IMAGE_STORAGE.md) adds 18 real file/WIC cases: twelve
complete original Mac storage observations plus six Windows ACL, identity, publication and path
admission regressions. No editor import or archive media transaction is connected yet.
