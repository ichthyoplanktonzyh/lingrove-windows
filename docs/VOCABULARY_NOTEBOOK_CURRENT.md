# Current stable notebook browsing

Reference: committed Mac `cc468b89f5c360cc7210fac4f1a8b52197870bf2`,
`Cotabby/UI/Vocabulary/VocabularyNotebookView.swift`: `listBrowser`, `activateHeadword`,
`sidebarFilterBar`, selected-entry reconciliation and the three sidebar row views. This adapts
the existing Mac browsing flow; vocabulary identity, search policy and authored records retain
their existing boundaries.

## Visible behavior

An entry with one visible sense is directly selectable, with no extra disclosure step.
Compact rows keep one fixed headword line; selecting one adds an inline meaning without changing
height. Detailed rows add a second meaning line. A multi-sense header shows its combined summary
in detailed mode; children remain numbered single-line meanings with usage counts. An example
snippet appears when the whole trimmed query matches an example or its translation and is not
already explained by the corresponding headword/title, following Mac's separate row rules.

Clicking a multi-sense headword expands it and selects the first visible sense if selection was
outside that entry. Clicking an already expanded selected entry collapses it while preserving
the exact sense. Other entries stay expanded. A different externally selected multi-sense entry,
including graph selection, reveals its child before returning to the list. Directed/cold-load
navigation and merged-ID resolution continue through the full acknowledged Store. The Windows
keyboard adapter uses focused-header Enter/Space; leaves and checklist controls keep their keys.

Language, tag, density and sort controls now belong to the browser sidebar. Density uses the
original `vocabularyListDensity` key, defaults to `compact`, and treats only exact `detailed`
as detailed. Unknown strings round-trip unchanged until toggled. General settings reset preserves
this independent preference. Selection, expansion, filters and sort remain window state.

## Ownership and asynchronous adaptation

AppHost supplies one `VocabularyNotebookListPreferences` over its existing settings owner. The
adapter follows settings replacement and persists only density; it never applies engine settings
or starts a model. Embedded/test windows can use an explicit transient window preference.

`VocabularyBrowserRows` borrows immutable acknowledged entries. Full `Senses` still supplies batch
and export identities; only `Children` is empty for a singleton. That same singleton sense owns
its checklist and menu receipt. Selection updates existing row containers in place. Detail/edit
still resolves against the full archive when a tag filter projects fewer senses.

`VocabularyNotebookWindow.List` owns list presentation/interaction. Snippets run in the existing
cancellable browse worker. Refresh disables retained rows until the latest projection arrives;
header and selection callbacks also check membership and offer version. A stale row cannot select
a removed sense during replacement. Batch/menu receipts keep their separate revision checks.

## Evidence and limits

Source reading and independent review compare committed current Mac methods directly. Existing
versioned notebook policies/fixtures are unchanged. One portable preference case covers malformed,
unknown and detailed values, restart/general reset and temporary save failure. The full portable
App suite passes1222 cases. The Release Windows-targeting solution compiles with zero warnings/errors.

Three compiled WPF cases cover routed singleton/header activation, compact height, focused-header
Space, exact multi-sense identity, density reopen, snippets, singleton checklists and a removed
sense behind a gated refresh. The existing graph case checks return-to-list expansion. Windows CI
must execute these final-head cases before merge; cross-compilation does not execute WPF.

Physical pointer/keyboard/accessibility interaction, typography and pixels remain Windows acceptance
gates. Existing search-collation gaps, native compact language names/localization, floating graph HUD
styling, review preparation/batch presentation and speech integration remain separate work.
No model, network request, installer or downloadable package is introduced.
