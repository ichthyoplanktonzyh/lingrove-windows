# Stable notebook graph integration

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/UI/Vocabulary/VocabularyGraphView.swift` and the mode/filter/selection/layout closures in
`VocabularyNotebookView.swift`. This round connects the already ported original graph libraries
and canvas to the existing notebook; it does not introduce a new graph product or data model.

| Source responsibility | Windows mapping |
| --- | --- |
| List/graph AppStorage mode and shared sense selection | `VocabularyNotebookWindow.Graph.cs`, existing notebook XAML/controller |
| Graph settings popover, legend and camera commands | `Graph/VocabularyGraphView.cs` |
| Persistent mode/Data and window-local graph state | existing `VocabularyGraphPreferences` / `VocabularyGraphPresentation` |
| Settings ownership/injection and reset scope | `Hosting/AppHost.cs`, `Settings/AppSettings.cs` |

## Ownership and flow

AppHost injects one narrow preference adapter through the existing reusable window controller.
Its getter follows the current settings owner; its save callback writes settings without applying
autocomplete/runtime settings or warming a model. One presentation and its original serialized
worker live for the notebook lifetime. GraphView owns controls and popup lifetime; the canvas owns
drawing/input; the existing Store owns all vocabulary and disk operations. No second Store, learning
service or inference runtime is constructed by the graph.

The existing latest-wins browse worker supplies filtered entries and query to both list and graph.
Graph nodes retain stable entry/sense IDs. Graph selection resolves against the current acknowledged
Store and drives the existing detail panel. Headwords toggle owned senses and clear selection;
exact senses reveal their parent and select the shared detail. Closing graph detail clears selection
and focus, retaining expansion. Graph detail is optional and uses the original 300/360/480 widths;
graph browsing has a 350 minimum. Same-mode redraws retain user-resized split widths. Sort is shown
only in list mode, as in the original SwiftUI view.

Entering graph reveals the current sense. Query/language/tag changes made in graph reconcile that
exact selected sense against the filtered graph, including a tag filter that leaves its headword but
removes that sense. List filtering keeps global detail. Entering graph with an already filtered-out
list selection does not add an extra reconciliation absent from source. Entering batch selection
forces list mode and retains checked sense IDs across filters/modes; explicit graph switching while
batch state already exists does not silently discard it.

### Asynchronous Windows filtering

Mac recomputes visible entries and reconciles graph selection synchronously. Windows retains the
existing off-dispatcher filtering boundary. A pending graph-filter reconciliation therefore carries
its original selected identity through superseding Store/sort/mode refreshes until the latest result
applies. Each pending graph edit retains its original document/query/language/tag criterion:
a later list filter cannot change its outcome, and a later including graph filter cannot undo an
earlier excluding one before selection clears. A later explicit selection, directed Reveal or
detail-close cancels that pending intent. Admitting an explicit detail mutation retires earlier
constraints before Store can publish Changed. This claims the command's detail intent even if the
operation fails; it does not select unacknowledged values. A pre-merge filter cannot follow a new
alias and clear the target between commit notification and command acknowledgement.
Stale worker replies cannot publish or clear that later choice.

During pending browse, the old drawing can remain visible for continuity but graph controls and
node intents are disabled. Virtual peers report disabled and reject direct Invoke; queued invocation
rechecks on the dispatcher. The latest accepted browse publishes the graph and enables interaction.
A failed browse leaves graph interaction unavailable and offers Retry alongside the fixed error.
Retry runs the current filter again; only a successful latest refresh enables the graph. This
failure/retry boundary is a scheduling adaptation, not synchronous Mac execution.

## Controls, preferences and lifetime

The settings popover exposes original appearance/force ranges, three label modes and defaults.
Changes normalize and save through the existing codec/presentation. Zoom out/in, Fit, Locate and
Collapse use original command identity. The empty graph uses original “No matching expressions”,
including cold/empty input; no list-specific loading wording replaces the graph source.

General settings Reset preserves both raw notebook preference values, including malformed/future
values. This matches Mac's explicit SuggestionSettingsStore reset key set, which excludes the
independent UserDefaults graph keys. A new portable reset factory makes that production rule directly
testable without constructing global input, UIA, model or AppHost services.

Ordinary Close hides the existing window, closes the graph popup and suspends layout; mode, expansion
and camera remain in the same presentation. Directed Show resets existing browse filters and reveals
the exact target. Retirement unsubscribes graph presentation updates, closes its popup and joins the
canvas's tracked work before the Store owner is disposed.

## Evidence and remaining verification

Twenty-one new compiled WPF cases use actual XAML, controls/popups, graph/presentation/worker, memory
SQLite and the shared Store. They cover durable controller startup, selection/detail sharing,
query/tag/language reconciliation, mode entry, head/sense intents, all settings/defaults, camera
commands, hide/reopen/retirement, checklist identity, merge/delete, split-width retention and gated
filter/mode/navigation races with disabled/hidden virtual peers, original filter criteria across
superseding list/graph edits, plus current-failure/Retry recovery. Three portable App cases execute the
actual reset factory over valid, malformed and wrong-shaped raw preference values, reparse saved
settings and check the live preference adapter follows replacement.

These are Windows integration tests and manually mapped source UI closures, not a new SwiftUI
runtime oracle. The forty-two existing actual-source verifiers remain unchanged. Expected full
Windows totals are Core3339 / Inference539 / App166 / WPF497. Cross-compilation does not execute
WPF. Exact-head Windows execution and the complete Mac CI must pass before merge.

Physical pointer/key dispatch, touchpad/magnify, native popup/splitter placement, external UIA/
Narrator, compositor occlusion, DPI/font/pixel comparison and persisted window geometry remain
host gates described in the canvas document. No model, network request, installer, artifact upload
or release participates in this round.
