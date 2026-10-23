# Export preview and save session ownership

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyExportSession.swift` and the original
`VocabularyStore.prepareExport` / `writeExport` delegates. The export sheet/picker is separate.

`VocabularyStore.Export.cs` borrows the single app-owned archive through the existing admitted media
operation owner. Offered selection IDs detach synchronously at archive admission. Shutdown tracks
preparation/save tasks and drains them before the archive closes. Export does not apply a notebook
revision or overwrite shared Store errors; errors propagate to the particular export session.
No second Store, archive, runtime or listener is constructed by these methods.

`VocabularyExportSession` is owned by one [export window](VOCABULARY_EXPORT_WINDOW.md) and confined to its WPF dispatcher,
adapting Mac MainActor. It retains a frozen request and current immutable configuration/scope key.
Preparation clears the previous package/message, advances generation identity and sets loading;
the Store prepares off the dispatcher. A reply publishes only if the session remains active, its
generation is still current, its captured key still equals the controls, and cancellation has not
been requested. The finally block clears loading only for that generation. A delayed failure cannot
overwrite a newer successful preview. Scope/configuration changes immediately make `CanExport`
false, even before the window starts the next preparation.

Save requires a current prepared key, the exact preparation ID and matching configuration, and
rejects another save while saving. The session passes that captured package to the Store, rather
than rebuilding against live notebook data. A save acknowledged by the archive remains successful
if the sheet closes or cancellation arrives after publication. Errors stay local and permit retry;
cancellation creates no error message. Cancel makes the session inactive, advances generation and
clears its package. As in Mac, closing during a preparation does not reset that retired generation's
loading bit; the closed session cannot publish or export, and the window no longer renders it.

Sixteen fixture probes execute the complete original Mac session, the existing original Store
wrapper extended with its actual prepare/write delegate bodies, and complete original archive and
encoders. App-specific diagnostics are omitted with exact-source guards; no exercised method body
is replaced. A test-only generation read and bounded archive gate control ordering. Blocking semaphore waits
run on the archive or detached test worker while MainActor remains responsive; the gate supplies
no replacement production result. Cases cover initial state,
selected/filtered/stale/empty scopes, changed controls, wrong package, actual save, close, cancelled
prepare/save and empty-before-cancellation precedence, close/key changes while preparing and latest
preparation ownership. The source save creates/removes only its private temporary file.

Windows CI compares those sixteen states using the production dispatcher session, actual memory
SQLite/Store/archive, and actual native save for the success case. For queued cases its test delegate
waits before calling the real Store, while the Mac gate waits on the actual archive actor; both
control response ordering without replacing selection/encoding. The small session corpus observes
state and file existence, while the separate [save suite](VOCABULARY_EXPORT_SAVE.md) establishes full
output bytes and native effects. Seven additional controlled-delegate cases cover late failure,
package/path forwarding, save reentry, close-after-admission acknowledgement, local failure/retry,
revocation before the next preparation, and prepare/save completion after an owned dispatcher exits.
Those two tests retire a separate STA dispatcher without creating another WPF Application; archive
acknowledgement finishes without waiting for an unpumped UI continuation. Dispatcher callbacks are
skipped after shutdown while all active-view state publication remains on its owner.
Delegate acknowledgement cases are state-machine tests,
not additional filesystem or desktop execution claims.

```sh
python3 scripts/verify-vocabulary-export-session-macos-parity.py --mac-repo /path/to/mac-repo
# Native Windows CI dispatcher suite:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExportSessionTests
```

The [notebook command/form adapter](VOCABULARY_EXPORT_WINDOW.md) now connects frozen offers,
controls, native picker ownership and close/task wiring with separate evidence. This session introduces no model,
clipboard or network operation. Full backup/restore and native desktop UIA/IME/ghost/insertion/GPU
validation remain separate gaps.
