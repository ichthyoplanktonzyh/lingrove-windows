# Vocabulary notebook browser

The tray's Vocabulary command and a saved favorite's Show-in-vocabulary command now open one
activating, reusable notebook window. This slice adapts the Mac list/detail browser: expression
groups contain stable sense rows; details show acknowledged meanings, authored text, notes, tags,
alternate wordings, full literal examples, translations and exact original-range highlighting.
Ordinary close hides the window and preserves browsing. An explicit Show command resets filters
and selects the requested sense, including a merged sense's alias after a cold archive load.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac responsibility | Windows mapping |
| --- | --- |
| `UI/Vocabulary/VocabularyNotebookView.swift`: list projection, global selection, literal detail/highlight | `Core/Vocabulary/VocabularyNotebookPolicy.cs`, `App/UI/Vocabulary/VocabularyNotebookWindow.xaml(.cs)`, `VocabularyBrowserRows.cs` |
| `Support/Vocabulary/VocabularyPolicy.swift`: searchable fields, all-term matching | `VocabularyNotebookPolicy.Matches`, narrow `IVocabularyTextSearching` contract |
| `Support/Vocabulary/VocabularySensePolicy.swift`: sense title and merged-ID resolution | `VocabularyNotebookPolicy.DisplayTitle`, existing `VocabularySensePolicy.Index` |
| Foundation `localizedStandardContains` | `Win32/Vocabulary/WindowsVocabularyTextSearch.cs`, managed culture-aware case/diacritic comparison with explicit platform gaps below |
| `Services/Presentation/VocabularyWindowController.swift`: one activating window and shared route | `App/Vocabulary/VocabularyWindowController.cs`, owned once by AppHost and disposed before the shared Store |

The window borrows the existing Store; it creates no database, capture listener or inference
service. Store notifications capture the immutable graph, loaded state and error together. A
worker builds tags and language/tag/search/sort projections with a cancellation token and version;
only the latest result can replace WPF rows. Shutdown closes admission, cancels and joins admitted
filtering before the Store closes. Virtualized rows own only presentation expansion/selection.

A tag-filtered entry is never an edit baseline. Details resolve stable sense identity against the
full graph, even if the list filter hides that row. Unrelated saves preserve unchanged literal
controls and scroll/selection. Saved examples use their archive-valid original ranges, independently
of stricter model-input budgets, so long saved text is not silently truncated for rendering.
Markup remains text. Selecting, searching or opening the browser does not start a model or transmit
notebook content.

## Evidence and platform limits

`vocabulary-notebook-v1.json` contains 51 observations from executed original pinned Mac methods:
search field scope, independent query terms, canonical/case/diacritic cases, tag/language filtering,
sort, title fallback, global/merged identity and actual SwiftUI AttributedString highlight runs,
including a 12,004-character example. The nineteenth Swift verifier compiles original methods;
its wrapper supplies fixed input values and exposes private results without substituting a policy.
It also records actual Foundation substring facts independently of the original expected outcome.
C# pure policy tests consume those explicit facts. Ordinary verification never rewrites expected
values, source metadata or native-provider scope.

The separate real .NET comparison subset covers 26 cases on both CI operating systems. Three
observed differences remain: `.NET` matches `œuvre` against `oeuvre`, and can ignore an internal
U+200B in either text or query where Foundation does not. These are flagged provider gaps, not
successful native parity tests. The verifier locks those three excluded IDs and the 26-case subset;
it does not skip any of the 51 policy observations. No Unicode rewrite table or approximate pure
replacement is used to hide those differences. Full locale/Unicode search parity remains open.

Six additional Windows WPF tests use the real memory SQLite archive and Store, compiled XAML and
an STA dispatcher: literal content/long ranges/activating native style/nonempty in-memory paint;
filters and ordinary versus directed reopening; cold load and non-replayed selection; merge alias
and deletion; latest-wins search and draining blocked work; and the shared route's single-window
lifetime. Cross-compilation on macOS cannot execute these tests. Windows CI executes them before
merge. These cases do not establish physical keyboard/pointer interaction or external-editor focus.

## Remaining notebook work

The list/detail browser displays acknowledged values. The [owned editor](VOCABULARY_EDITOR.md)
now supplies an explicit full-value draft and acknowledged Save/Cancel. Delete/merge/move/new-sense controls, explicit
explanation adoption, the notebook-owned AI/dictionary session, speech, review/Anki, batch selection,
graph mode, export/backup/restore, localization and persisted window geometry remain unported UI.
Authored image references display an unavailable placeholder and literal caption until verified
media preparation/storage is implemented. Source-application attribution is also pending: current
Windows captures leave application identity absent rather than substituting a process basename.
Existing stored source identifiers are retained but not resolved/displayed by this window.

The private SQLite file adapter and archive restart tests remain their own evidence. The memory
view suite does not prove native file durability, Windows UIA/IME, ghost placement, insertion
acknowledgement or GPU/model execution. No installer, artifact upload or release is produced.
