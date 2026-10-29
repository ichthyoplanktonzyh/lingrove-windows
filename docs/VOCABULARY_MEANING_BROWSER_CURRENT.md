# Frozen meaning and category browser component

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`:

- `Lingrove/Support/Vocabulary/VocabularyMeaningLabelPolicy.swift`, `VocabularyMeaningBrowserProjection.swift`, `VocabularyClassificationProjection.swift`, `VocabularyPolicy.swift` (`matches`) and `VocabularySensePolicy.swift` (`displayTitle`).
- `Lingrove/UI/Vocabulary/VocabularyMeaningSidebar.swift`, `VocabularyCategorySidebar.swift`, `VocabularyNotebookView.swift`, `VocabularySemanticPresentation.swift`, and `Lingrove/UI/Components/LingroveBrand.swift`.
- `LingroveTests/Support/Vocabulary/VocabularyMeaningLabelPolicyTests.swift`, `VocabularyClassificationTests.swift`, `LingroveTests/UI/Vocabulary/VocabularyMeaningViewTests.swift`, `VocabularyClassificationViewTests.swift`.

## Boundaries

`Core/Vocabulary/Notebook` projects the caller's collected resource entries and detached semantic/classification index. Label preference preserves an existing label's identity and language; it selects no translation. Meaning search checks labels, definitions, scope, confirmed cues, sense fields, confirmed patterns and mapping restrictions without inferring an edge. Confirmed mappings provide distinct sense selections; proposed mappings only contribute pending counts. Unclassified senses retain their IDs. Category paths preserve multiparent rows, ancestor search, hidden/retired filtering, expanded paths and the 10,000-row traversal bound; counts deduplicate confirmed meaning IDs. Callers supply `IVocabularyTextSearching` and a natural title comparer, because Foundation's locale behavior is a platform boundary. The meaning item copies selection values and retains Swift-style value equality.

The unactivated WPF `VocabularyMeaningSidebar` and `VocabularyCategorySidebar` accept real immutable snapshots/projections and localized text providers from their owner. They hold only visual selection, search and disclosure state. They emit selection, create, edit and manage intents; they do not instantiate a Store, persist changes or fabricate a preview. The eventual owner must refresh snapshots after acknowledged writes and implement the dialogs, localized compact language and sense labels, and platform search/comparison adapters. No new application entry point is wired here. `VocabularyFrozenDocument` remains an isolated value boundary; its durable codec/migration and Store activation are not established by this component.

## UI evidence and platform adaptation

The Mac meaning list uses concept/function symbols, a semibold 14.5-point title, a count capsule and pending orange dot; selected compact rows show an inline expression/definition preview and detailed rows show a secondary 13-point preview. Other expressions remain a disclosure section with individual sense identity, a language badge and selected/detailed meaning previews. The category column has a 12-point padded title and create/manage actions, search, all/uncategorized filters, content/function headings, 12.5-point hierarchy, 12-point indentation per depth (capped at eight), counts, and contextual edit. Category search exposes matching ancestors and selection reveal expands the first visible parent path. Core rules and callbacks preserve these responsibilities; Windows theme controls, glyph font and vector approximations replace AppKit/SF Symbols. System font fallback, native selection colors, window chrome and DPI differ from Mac. Text scaling is supplied by the owner.

WPF list rows, accessible names, focus restoration and Left/Right category disclosure are implemented. Enter/Space/Left/Right operate the other-expressions disclosure. The offscreen test harness exercises selection, filtering, create/edit/manage callbacks, hidden-selection reset, category Left/Right routed keys and empty/detailed/selected renders. It does not establish screen-reader expand/collapse patterns, actual physical focus/keyboard behavior, Windows high-contrast appearance, resize, localization or native desktop acceptance. The current implementation materializes rows into WPF list controls; large-catalog responsiveness and virtualization require physical/integration profiling before claiming complete UI acceptance. Exact SF Symbol and Mac pixel equivalence are not claimed.

No matching Mac screenshot of these particular sidebars was found in the frozen committed assets; the Mac source and UI tests above are the design evidence. Adjacent committed creation/import references are `.github/assets/vocabulary-entry-import/create-entry-zh-Hans.png` and `.github/assets/vocabulary-import-examples/import-preview-zh-Hans.png`; the older `.github/assets/vocabulary-entry-import/import-preview-zh-Hans.png` predates example columns. `LINGROVE_TEST_RENDER_DIR` can be set for the WPF suite to save representative 300 × 440 PNGs; these are Windows component renders, not a pixel comparison or physical-desktop proof.

## Verification

On Windows x64 with SDK 10.0.402:

- `dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyMeaningBrowserProjectionTests|FullyQualifiedName~VocabularyMeaningLabelPolicyTests'` passed 8/8.
- `dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyMeaningSidebarViewTests` passed 3/3, with offscreen renders enabled using `LINGROVE_TEST_RENDER_DIR`.
- `dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --no-build` passed 4665/4665.
- `dotnet build Cotabby.sln --configuration Release` passed with zero warnings and errors.

Native Swift execution, live Store integration and final-head CI remain separate gates. No endpoint, model, notebook file, installer or package was used.
