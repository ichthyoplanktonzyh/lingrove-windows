# Backup and restore file panels

Reference: pinned macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Vocabulary/VocabularyNotebookView.swift`, `backup()` and `chooseBackup()`.
The save panel uses the `lingrove` type and `Lingrove-vocabulary.lingrove` filename; the open
panel chooses one backup. The view's separate `Replace notebook?` alert still precedes restore.

`WindowsVocabularyBackupDialog` adapts those two panels and fixes their type/default. It returns
only a filesystem path. A notebook command owns one invocation; selecting a restore source
does not itself authorize archive replacement. The [notebook commands](VOCABULARY_BACKUP_UI.md)
now connect these panels and the owned explicit replacement confirmation.

`WindowsVocabularyFileDialog` now owns the shared native thread/cancellation/COM boundary that
previously lived in `WindowsVocabularyExportDialog`. The export facade retains its captured type,
filename, public methods and save flags; its existing eleven startup/admission cases remain
unchanged. The internal owner holds one Shell dialog on its creating STA, marshals cancellation
back to that thread, requires a nonzero owner before Show, and releases returned item/path/RCWs.
It is single-use and creates no listener or process-wide service.

Save retains overwrite prompting, strict type, existing parent, filesystem-only, no-current-folder
change, no-readonly-return and no-test-file-create flags. The last flag prevents the Shell probing
a protected database/media destination before the archive's actual destination guard. Restore
requires an existing filesystem item and clears folder, nonstorage, multi-selection, no-validation
and no-readonly-return flags. A read-only backup is valid input, unlike a read-only save destination.
It leaves the open-panel filename unset. The nullable filename in the noninteractive probe records
that no default was supplied; it does not inspect a selected filename before Show.

These are the SDK's [Common Item Dialog](https://learn.microsoft.com/en-us/windows/win32/shell/common-file-dialog)
and [FILEOPENDIALOGOPTIONS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_fileopendialogoptions)
contracts. File type selection is presentation; all actual source/destination validation, bounds
and writes remain in the archive adapters.

Twelve new Windows cases instantiate the real Save/Open COM implementations on the owned STA
and verify configuration, unchanged current directory, pre-cancelled/disposed/unowned admission,
foreign-thread rejection and construction outside STA. They never invoke native Show. Together
with the unchanged export tests, they guard the extracted native boundary without altering runner
files or selecting a real source. Local Release solution cross-build passes with 0 warnings/errors;
native execution is pending CI. Physical Shell navigation/cancellation and the product confirmation
flow remain Windows desktop gates. No installer, artifact, model or network test is added.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI only, no modal Show:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyBackupDialogTests|FullyQualifiedName~VocabularyExportDialogTests'
```
