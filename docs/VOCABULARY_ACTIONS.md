# Notebook sense and usage actions

The notebook now adapts the stable Mac New sense, Merge sense, Delete sense and Move usage
commands. They call the existing shared Store/serialized archive; no second set of ownership or
mutation rules is introduced. A new sense receives an acknowledged stable UUID. Merge retains
target identity, aliases and annotations. Moving an example retains its ID and exact value, and
keeps even an empty source sense. Deletion requires an owned modal confirmation and names one
exact sense; deleting the last sense removes its entry through the existing archive rule.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source responsibility | Windows mapping |
| --- | --- |
| `UI/Vocabulary/VocabularyNotebookView.swift`: detail toolbar, explicit delete alert, merge and usage menus | `App/UI/Vocabulary/VocabularyNotebookWindow.Actions.cs`, header XAML and `VocabularyDeleteSenseWindow.xaml(.cs)` |
| `Services/Vocabulary/VocabularyStore.swift`: create/merge/remove/move calls and fixed failures | Existing app-owned `VocabularyStore` methods |
| `Services/Vocabulary/VocabularyArchive.swift`, `Support/Vocabulary/VocabularySensePolicy.swift` | Existing serialized archive, three-way draft and sense ownership policies; no new Core algorithm |

The browser partial owns command presentation, receipts and pending UI state. The confirmation
window owns only the user's answer. Commands resolve the selected identity against the complete
acknowledged graph rather than a tag-filtered projection. Menu items carry entry/sense/usage IDs,
the detail version and a menu epoch. Target change or hide retires old menus; closing also detaches
them from their buttons so right-click cannot reopen an expired menu after ordinary reopening.

While one mutation is pending, command controls disable. The list can still be browsed. A completion
changes selection only when both the captured identity and detail version remain current; visiting
another sense and returning is a later browsing choice too. This is a Windows presentation guard,
not a change to archive ownership. A Store notification can independently resolve a merged alias
or clear a removed selection. Failure preserves the archive and shows its fixed error.

Each command task is tracked immediately. Retirement cancels presentation callbacks, closes an
open confirmation, and joins admitted transactions before Store disposal. An accepted mutation is
not canceled just because its window closes. The action partial keeps this lifecycle separate
from search/filter workers and literal detail rendering. None of these commands invokes a model,
dictionary, clipboard, endpoint, media write or export.

## Evidence and remaining work

The existing actual Mac sense/actor corpora continue to verify create/merge/move/remove rules.
This UI slice introduces no replacement Swift oracle or new expected mutation algorithm.
Eight added Windows WPF cases use the real memory SQLite archive and shared Store: acknowledged
new identity; merge aliases/usage IDs and replay rejection; moving to existing/new sense and
retaining the empty source; actual confirmation Cancel/Delete including last-sense removal;
retired menus/buttons on navigation/hide; a missing move target's fixed failure and recovery;
a later visit back to the original sense during a blocked mutation; and draining an admitted
mutation on retirement. A controlled clock schedules the actual archive operation. The command
bar wraps at narrow window widths.

Windows CI executes these cases before merge; macOS can only compile them. The total WPF suite
is now 62 cases. Routed events do not prove physical pointer/keyboard or native popup focus. Named
file protection/durability retain their separate tests. Native UIA/IME, ghost pixels, insertion
acknowledgement and GPU/model execution are not established by this suite.

Notebook AI/dictionary sessions, explicit explanation adoption, speech, image preparation/storage,
copy-expression integration, review/batch/graph/export/backup/restore, localization and persisted
geometry remain open. The three documented Unicode search-provider differences also remain.
No installer, artifact upload or release is produced.
