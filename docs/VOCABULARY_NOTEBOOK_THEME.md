# Canonical notebook visual adaptation

Reference revision: macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. The hierarchy and states are traced to `Lingrove/UI/Vocabulary/VocabularyNotebookView.swift`, `VocabularyMeaningDetail.swift`, `VocabularyMeaningSidebar.swift`, `VocabularyCategorySidebar.swift`, `VocabularyEntryCreationSheet.swift`, `VocabularyWordListImportSheet.swift`, and the corresponding `LingroveTests/UI/Vocabulary/VocabularyNotebookViewTests.swift`, `VocabularyMeaningViewTests.swift`, `VocabularyClassificationViewTests.swift`, and `VocabularyEntryCreationViewTests.swift`. The adjacent committed sheet references are `.github/assets/vocabulary-entry-import/create-entry-zh-Hans.png` and `.github/assets/vocabulary-import-examples/import-preview-zh-Hans.png`. No matching Mac image for this exact browser was found; comparison of its hierarchy uses the source views and tests, not a claimed screenshot or pixel match.

The canonical notebook and its creation/import sheets opt into `VocabularyNotebookTheme`, a window-scoped adapter of the existing `UI/Theme.xaml`, `WindowTheme` and `Ui` conventions. It follows Windows light/dark app settings, uses Segoe UI Variable/Segoe UI fallback, native title chrome, 96-DPI WPF units, shared rounded inputs/buttons/cards, thinner draggable splitters, clear selected borders, disabled and error states, and a wrapping toolbar so narrow windows retain their main operations. Input defaults are grouped in a horizontal row to keep the import result table visible. Existing semantic/category sidebars opt in only from the canonical browser; their prior callers retain their default appearance. The changed templates retain TreeView selection, disclosure and keyboard interaction, and the existing element names, actions, information hierarchy and input/session ownership are unchanged.

When Windows high contrast is active, the adapter maps backgrounds, text, borders, accent and error resources to system colors. Its selected row keeps a visible accent edge on the system background. Offscreen captures exercised light, dark, and a **simulated** black/white/yellow high-contrast palette; they did not change the host's actual Windows accessibility setting. The generated images are under `%TEMP%\frozen-notebook-themed` with `-light`, `-dark` and `-highcontrast-simulated` suffixes: empty, selected, meaning, meaning-sense, import, creation, error, multisense and 760/800-pixel narrow states. These renders were visually inspected for legibility and hierarchy. Real system high-contrast palettes, title-bar behavior, theme change events, DPI scaling, screen readers and physical desktop interaction remain acceptance work. No claim of complete visual parity or physical acceptance is made.

On Windows x64, `dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyWindowControllerTests|FullyQualifiedName~FrozenVocabularyNotebookWindowTests|FullyQualifiedName~VocabularyMeaningSidebarViewTests|FullyQualifiedName~FrozenVocabularyEntryInputIntegrationTests|FullyQualifiedName~VocabularyEntryImportViewTests|FullyQualifiedName~WindowsVocabularyWordListReaderTests'` passed 38/38, with the above optional render directory enabled. The tests include a real TreeView disclosure binding and narrow meaning-toolbar visibility in addition to existing Store, input and lifetime checks. This is an isolated style checkpoint; it does not change global AppTheme, AppHost, storage, models, endpoint behavior or unsupported product actions.

The broader dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build passed 582/582 on the immediately preceding compiled snapshot; afterward only TreeView automation names and render-helper background composition were refined, then the 38 focused tests were rebuilt and rerun. dotnet build Cotabby.sln -c Release --no-restore passed with zero warnings and errors before the final keyboard test-only assertion.

Independent review inspected the final source and representative narrow/dark/import/error/simulated
high-contrast images and reran the focused suite 38/38. After integration with the canonical AppHost
and review transaction boundary, Windows x64 .NET SDK 10.0.403 executed:

```powershell
dotnet build Cotabby.sln -c Release --no-restore --verbosity quiet
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build --no-restore --blame-hang-timeout 2m --verbosity minimal
```

The solution rebuilt with zero warnings/errors and the complete compiled WPF suite passed 585/585.
The render helper fills the window's actual background before drawing its WPF content, avoiding
transparent bitmap margins; the images use no postprocessing. These checks do not establish the
remaining physical desktop, DPI or accessibility acceptance above.

The canonical theme additionally scopes a WPF ScrollBar template to opt-in windows, including the
entry editor and creation/import sheets. Its native `Track`, `Thumb`, line and page commands retain
vertical/horizontal directions and scrolling input; track, thumb and hover colors follow the window
palette or Windows system high-contrast colors. Other windows and non-opt-in sidebar callers do not
receive this style. An STA control test exercises wheel, Page Down, line/page commands and thumb drag
on both axes; optional light/dark/simulated-high-contrast captures include the image editor and
narrow editor. Physical pointer, real system high contrast, screen reader and DPI acceptance remain
open.
