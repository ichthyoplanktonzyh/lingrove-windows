# Notebook image paste

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Vocabulary/VocabularyContentEditor.swift` and
`Cotabby/Services/Vocabulary/VocabularyImageTransfer.swift`.

The editor now connects Mac's dedicated Paste image action and explanation-section paste intent
to the [encoded-image transfer library](VOCABULARY_CLIPBOARD_TRANSFER.md). This adapts the existing
import flow; it adds no second archive or alternate image preparation policy.

AppHost creates one `WindowsVocabularyClipboardTransfer`, lends its narrow
`IVocabularyClipboardImageTransfer` interface through the notebook controller/window to editors,
and cancels/drains it after retiring notebook presentation and before disposing Store. Windows
and editors never construct their own extraction service. Construction reads no clipboard data.

`VocabularyEntryEditorWindow.Clipboard.cs` owns only paste intent. The button captures a clipboard
offer and starts the existing import operation. The explanation section also handles the WPF Paste
command's tunnelling preview events, before a TextBox's ordinary paste handler can request a
foreign data object. Capture checks only registered format availability and a stable sequence;
it never opens the clipboard or requests data. Image offers are consumed inside this section,
including captions. Text-only offers fall through, and other fields retain their normal command
route. This does not install a global paste shortcut or change the autocomplete/input monitor.

The captured sequence travels to the helper rather than being recaptured after scheduling. A
missing/stale offer or null detached response adds nothing and shows no failure. A non-null empty
or malformed response is selected data: it passes to normal preparation, reports the fixed import
failure and never falls back to another format. PNG/TIFF/JPEG precedence remains in the reader.

The common import factory now starts away from WPF. Clipboard replies are privately owned, so
Store's defensive byte copy and normalization admission can run there too. The separate borrowed-
memory entrypoint retains synchronous Store detachment before its caller can mutate the input.
Dispatcher callbacks validate the proposed content, append a stable block with an empty caption
and retain its prepared bytes, or update local import status/failure. Save/Add/Paste
are disabled while importing; existing block edits/delete/reorder remain available. Duplicate
image occurrences still count individually toward the sense budget, and the map is pruned only
after its final matching reference is removed. Cancel writes no media. Save uses the existing
acknowledged media/SQLite transaction and preserves the map for a failed-save retry.

Closing cancels the editor wait and drops late results. Import continuation/cleanup no longer
depends on a live dispatcher: presentation is posted with the lifetime token, cancellation skips
it, and retirement joins both save and import waits. Token-source disposal separately waits for
the underlying factory, which may still be using its token after the UI wait is cancelled. The shared transfer owner separately drains
its actual child work; Store separately drains any already admitted bounded preparation. This
retains Mac's cancellation behavior without stranding shutdown on a retired UI continuation.

Twelve WPF cases use actual controls, routed Paste commands, WIC normalization and SQLite with a
test clipboard-offer boundary. They verify acknowledged Save, borrowed-buffer detachment, worker admission, command capture
before TextBox paste, text-only and outside-section command fallthrough, no-offer/null quiet behavior, empty/invalid/transfer failure, busy admission,
Cancel before a late provider result and the 64-block limit. One reader case verifies that format
admission makes no open/data/lock calls. These fixtures never read or replace the runner clipboard.
They do not prove physical Ctrl+V/IME behavior, real producer interoperation or AppHost desktop
shutdown. Existing file/image tests continue to cover budgets, pruning, rollback and preview.

Encoded PNG/TIFF/JPEG retain precedence. The [DIB adapter](VOCABULARY_CLIPBOARD_DIB.md) now adds
supported packed `CF_DIBV5`/`CF_DIB` formats to this same flow. Compressed/profiled variants,
virtual/pixel-only drops and native Windows clipboard producer checks remain open. A successful
compile or fixture paste is not evidence that every Windows app exposes a supported image format.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Native Windows CI, without clipboard access:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyImagePasteTests
```
