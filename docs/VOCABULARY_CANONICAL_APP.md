# Current-format notebook composition

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, especially
`Lingrove/Services/Presentation/VocabularyWindowController.swift`,
`Lingrove/Services/Vocabulary/VocabularyStore.swift`,
`Lingrove/UI/Vocabulary/VocabularyNotebookRoot.swift`, and
`Lingrove/UI/Components/VocabularyFavoriteButton.swift`. The maintainer's current scope excludes
old-data compatibility and permits native Windows/WPF visual design while preserving behavior,
information architecture and state expression.

`AppHost` owns one `CanonicalVocabularyFeature`. It creates a lazy current-format Store at
`%LOCALAPPDATA%\Cotabby\vocabulary\vocabulary-v8.sqlite`, shares it with the writing bridge and
canonical notebook window, and disposes it after its borrowers. Construction, settings application
and unused shutdown do not open the notebook. The old `vocabulary.sqlite` is not discovered, read,
converted or replaced. The legacy archive, notebook, review and media classes remain library code;
they are not instantiated as an additional production notebook owner.

The tray's existing Vocabulary command now opens the canonical browser. It supports collected-entry
list/detail browsing, search, language/tag filters, ordering, persisted list density, meaning/category
navigation, manual creation and CSV/TXT preview/import. Writing sentence and dictionary favorites use
the same Store for acknowledged Save, exact Remove and Show-in-notebook. Saved explanations are
restored or attached through the shared explanation service without starting generation. Changing
the explicit explanation language updates display labels; the current English Windows interface
supplies the fallback. It does not rewrite authored languages or reset notebook selection.

Only an explicit notebook command activates its reusable window. Ordinary close preserves browsing.
Show-in-notebook reveals the acknowledged stable entry/sense and removes filters that hide it.
On shutdown, the host closes window admission and drains accepted input operations, retires the
explanation producer, then drains writing operations and closes the sole backend. The owning feature
releases the backend even if a borrower reports a disposal failure. Classification installation is
owned by the canonical Store's serialized transaction, not by a window or the model runtime.

The [canonical entry editor](VOCABULARY_CANONICAL_ENTRY_EDITOR_UI.md) now edits a selected existing
sense using the complete joined entry as its isolated baseline. Save waits for the same Store's
acknowledged three-way merge; conflict retains the draft and Cancel discards it. Authored fields,
content blocks and usages remain distinct from generated explanations. File-image preparation,
preview and persistence use the Store's identity-bound media transaction. Closing the modal or app
drains accepted saves and image/range preparation before releasing that owner.

The existing Anki connection, credential and default-deck settings retain their independent real
client. Canonical review/card/template transactions exist at the Store boundary; review presentation
and preparation remain unavailable. Graph, independent sense/category creation/merge/move/deletion,
image paste/drop/enlargement, full notebook explanation/dictionary rows, export/backup, review and
speech remain subsequent canonical modules. An older library's checks do not activate them.

Windows presentation uses WPF controls/system fonts, native window chrome and 96-DPI layout units.
The [window-scoped visual adaptation](VOCABULARY_NOTEBOOK_THEME.md) follows Windows light/dark
settings and maps high contrast to system colors; physical theme/DPI acceptance remains open.
The browser/component records describe rendered states and available Mac reference images:
[browser](VOCABULARY_NOTEBOOK_FROZEN_UI.md),
[writing bridge](VOCABULARY_CANONICAL_WRITING_UI.md), and
[Store](VOCABULARY_CANONICAL_WRITING_STORE.md).
Full physical keyboard/mouse, IME, DPI/high contrast and screen-reader acceptance remain separate.

The composition tests exercise the actual owning feature with isolated current-format files and an
old-name sentinel. They check inert startup, accepted-writing drain, reopen, stable usage identity,
bundled classification and untouched old files. They do not construct AppHost, hooks, live models,
endpoints or a user's notebook. Exact executed validation is recorded with the round's handoff;
this composition record alone is not a claim of completed desktop acceptance.

Combined validation on Windows x64 10.0.19045, .NET SDK 10.0.403:

```powershell
dotnet build Cotabby.sln -c Release --no-restore --verbosity quiet
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore --verbosity minimal
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --no-build --no-restore --verbosity minimal
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests' --verbosity minimal
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build --no-restore --blame-hang-timeout 2m --verbosity minimal
```

The build passed with zero warnings/errors. Core passed 4758/4758, portable App 1230/1230,
noninteractive Inference 628/628 and compiled WPF 585/585. Each test command executed a test
assembly and reported its count. The test processes disabled .NET telemetry and clipboard reads;
no live model, endpoint or existing notebook was used. Independent review identified a late
import-preparation drain case beyond the initial 583 passing WPF tests. The repair retained and
drained ordinarily cancelled child windows; independent focused checks passed 22/22, followed by
the zero-warning solution rebuild and complete 585/585 WPF run recorded above. Physical desktop
shutdown, Shell interaction and other native acceptance remain separate.
