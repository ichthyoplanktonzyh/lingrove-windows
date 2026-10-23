# Notebook export commands and Windows form

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Vocabulary/VocabularyNotebookView.swift` (`selections`, `beginExport`, checklist and
sense context menu), and `Cotabby/UI/Vocabulary/VocabularyExportSheet.swift`. This adapts those
existing flows; it introduces no new export format, product selection rule or hosted service.

`VocabularyExportOfferPolicy` receives a full acknowledged entry list, the current visible
projection, raw filters, sort direction and transient batch IDs. It preserves notebook order,
including sense order within each entry. Batch IDs hidden by a filter remain selected; stale batch
IDs drop out of the offered full graph. A single-sense command preserves that exact ID, overrides
the initial scope to Selected and does not expand merged aliases. Explicit scope takes precedence
over the automatic default; otherwise a nonempty batch chooses Selected, any raw filter chooses
Filtered, and a clear notebook chooses All. Whitespace search is still a raw filter, as on Mac.
The resulting request freezes all three lists for the form.

The notebook's `VocabularyNotebookWindow.Export.cs` owns checklist state, exact menu receipts and
one owned modal export window. Leaving checklist mode or changing filters preserves the batch set.
Select visible unions only the senses in the visible projection, including tag-reduced entries.
Batch selection never changes the durable Store or ordinary detail selection. WPF filtering runs
on a worker, so export commands and row checkboxes wait for the current projection/version to
publish. A Store revision change, filter refresh or hide invalidates old row/menu receipts. Menus
capture their sense ID when opened, rather than following a recycled visual's later DataContext.
An already opened export keeps its own request and prepared bytes through subsequent Store edits.
If a later preparation encounters a deleted offered sense, the existing Changed failure applies.

`VocabularyExportWindow` owns one dispatcher session and its cancellation sources. It borrows the
shared Store, presents the same scope/format/text-layout/content options, counts and bounded literal
preview, and starts preparation when the key changes. It captures all controls before session
notifications redraw them. Form controls freeze while choosing/saving; Cancel remains available
during preparation/save and is disabled during destination choice, matching the Mac sheet. A normal
close cannot orphan the chooser. Notebook/application retirement cancels it and joins admitted work.
Tasks are tracked before entering a modal native call; late picker replies cannot save after close,
against a changed key, or with a different prepared package. Errors stay in this form and allow retry.

`IVocabularyExportDestinationPicker` is a narrow human-decision seam. Its production implementation
uses one STA-owned `WindowsVocabularyExportDialog` wrapping Shell FileSaveDialog; it derives the
fixed filename/one extension from the captured configuration, uses the owning HWND and releases its
COM objects on that thread. Cancellation posts native Close to the same owner, with each callback
bound to its own invocation. Strict filesystem/type/path flags and `FOS_NOTESTFILECREATE` prevent
the Shell from probe-creating a chosen path before the archive protects database/media destinations.
Only the [existing atomic save adapter](VOCABULARY_EXPORT_SAVE.md) creates or replaces export files.
The interface/vtable follows the [Microsoft IFileDialog contract](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifiledialog)
and [dialog option flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_fileopendialogoptions).

Fifteen fixture observations execute the original pinned Mac `selections`/`beginExport` and
`visibleEntries` bodies, using the existing browse harness and actual export request values. A
reference-type test wrapper supplies SwiftUI's nonmutating State storage behavior without changing
those method bodies. Cases cover all/default/oldest/ties, raw and whitespace filters, tag-reduced
senses, language, forced/empty scopes, hidden batches and exact/stale single targets. Core compares
the same requests against those captured observations; CI recompiles the actual Mac source for
every run. Its small literal search adapter is explicit; broader collation differences remain in
the [notebook record](VOCABULARY_NOTEBOOK.md).

Twelve compiled WPF form/notebook cases exercise option changes, literal previews, picker cancel
and retry, a real native failed/successful atomic save, stale-package refusal, close/cancellation and
draining, empty scopes, persistent ordered batches, detached rows, exact/expired menus, frozen IDs
and notebook retirement, including empty feedback after deleting a retained batch. The human picker responses are controlled. Eleven separate native Shell
startup/configuration/admission cases instantiate and release the real COM dialog **without Show**.
They validate startup/ABI/configuration only. Physical Shell navigation, overwrite prompts, modal
cancellation and application shutdown with the real dialog still require a Windows desktop check.
Routed events do not establish physical pointer hit testing or visual parity with SwiftUI.

```sh
python3 scripts/verify-vocabulary-export-offer-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportOffer
# Windows, after solution build:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyExportWindowTests|FullyQualifiedName~VocabularyExportDialogTests'
```

Full notebook backup/restore, graph browsing, review/Anki and remaining learning providers remain
separate migration work. This export is learning material, not a restorable notebook backup.
Native UIA/IME/caret/ghost/insertion, real clipboard producers and CPU/GPU/model gates stay open.
Routine CI compiles and tests only; it produces no installer or downloadable package.
