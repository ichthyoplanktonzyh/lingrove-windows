# Existing-file image drops

This adds the Windows Shell's Unicode `WM_DROPFILES` path to the existing image editor. The pinned
Mac `VocabularyContentEditor.swift` at `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` chooses the first
provider and prefers its file URL, then prepares it through the shared archive. Windows now takes
only the first detached existing-file path and uses the same `ImportImageFile` entry as the picker.
An invalid first image fails with the fixed import message; later paths are never fallback inputs.

`WindowsVocabularyFileDrop` is owned by one editor HWND. It registers at source initialization,
with a Loaded fallback, then unregisters/removes its hook on close. The bounded Shell allocation is
copied before parsing: at most 1 MiB, a 20-byte `DROPFILES` header, an aligned in-bounds path offset,
client coordinates, a nonzero Unicode flag, a bounded terminated strict UTF-16 first path and a
fully qualified filename. ANSI, malformed and nonclient-coordinate payloads are ignored. This
adapter is not an arbitrary pointer decoder: invalid global handles fail size/lock admission.
It unlocks and calls `DragFinish` in `finally`, including rejected input, before publishing the
detached string/point. Callback failures cannot escape the native window procedure.

`VocabularyEntryEditorWindow.FileDrop.cs` converts client pixels through the HwndSource's actual
device-to-DIP transform, then performs a WPF visual hit test against `ExplanationDropArea` ancestry.
This respects scrolling clips and occluding controls; a transformed rectangle alone would also
accept invisible content behind the Save/Cancel footer. Hidden, disabled, saving, importing and
full editors reject admission. The existing import task owns the rest: bounded background file
read/normalization, local draft bytes, cancellation, validation, acknowledged Save and Cancel.

The native structure/message contracts come from Microsoft's [DROPFILES layout](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-dropfiles),
[WM_DROPFILES ownership](https://learn.microsoft.com/en-us/windows/win32/shell/wm-dropfiles),
[DragAcceptFiles](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-dragacceptfiles)
and [DragFinish](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-dragfinish).
The Shell documents an OLE compatibility path for real [`CF_HDROP` file locations](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard).
Virtual file contents use different provider protocols, so this does not claim full Mac
`NSItemProvider` or arbitrary OLE drag parity. Pixel-only image drags, virtual files, ANSI drops,
and elevated-host interoperability remain open. Encoded clipboard images now use the separate
[image paste adapter](VOCABULARY_IMAGE_PASTE.md), including the bounded [DIB subset](VOCABULARY_CLIPBOARD_DIB.md). No message filter is relaxed.

Twenty-one Windows cases use actual HGLOBAL allocation, `SendMessage` and the compiled WPF HWND
hook without touching the runner clipboard or a foreign provider. They verify first path and
memory release before callback, noncanonical nonzero BOOL values, malformed/oversized headers and
paths, no decode fallback, clipped footer/other-field rejection, supplied 100/150/200% coordinate
transforms with actual WPF hit testing, hidden/disabled editors and callback failures. Source file
IO and WIC normalization are real in the editor cases. The scale cases are synthetic transforms,
not a claim that the CI display was physically configured at every DPI. Real Explorer-to-app and
other producer interoperability still need a Windows desktop check.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Native Windows CI:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyFileDropTests
```
