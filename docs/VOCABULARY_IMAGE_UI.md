# Notebook image import and presentation

This connects explicit file import, draft previews, saved-image reading and enlargement to the
existing shared Store/archive. Reference: Mac pin `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Existing Mac responsibility | Windows adapter |
| --- | --- |
| `UI/Vocabulary/VocabularyContentEditor.swift`: single image picker, import admission, append only after validation | `VocabularyEntryEditorWindow.Images.cs`, Add image and the production `ImportImageFile` callback |
| `UI/Vocabulary/VocabularyEntryEditor.swift`: editor-local byte map, disable Save during import, Save/Cancel | Existing modal editor plus immutable `VocabularyPreparedImage` map passed to Store only on Save |
| `Services/Vocabulary/VocabularyStore.swift`: prepare/read/update facades | `VocabularyStore.Images.cs` plus synchronously detached Save map |
| `Services/Vocabulary/VocabularyArchive+Media.swift`: bounded selected-file read | Archive `PrepareImageFileAsync`, `WindowsVocabularyImageInput`, existing WIC preparation |
| `UI/Vocabulary/VocabularyContentView.swift`: shared lazy image row, unavailable/loading/enlargement | `VocabularyImageView` shared by the notebook reader and editor |

The file picker selects one image. Its Windows filter lists common raster extensions; actual WIC
codec availability decides decode support, so optional formats can fail with the existing fixed
import message. The selected source is read through one regular-file, no-follow handle with a
24 MiB ceiling enforced before allocation and during reading. An explicitly chosen source may
have shared read permissions; the protected notebook media ACL policy is unchanged. Original paths
and metadata do not enter the stored reference. Preparation remains off the dispatcher and writes
no notebook/media resources.

Import admission is synchronous: one import at a time, fewer than 64 blocks, no Save in progress.
Save and add controls stay disabled while import runs; deletion/reordering of existing blocks can
continue. Completion validates the complete proposed content before changing either blocks or
draft bytes. The sense's 16 MiB budget counts every image occurrence, even when one hash backs
several blocks. Sequential repeated imports are allowed and get distinct block UUIDs. The draft map
is keyed once per filename and pruned only when the last referencing block is removed. New image
captions start empty, and caption edits keep references unchanged. There is no new Replace action:
delete and add follow the original editor.

Cancel/close retires import admission immediately and discards the map. An already running bounded
native operation may finish in its archive owner, but cancellation and the retired guard prevent
late append. Save snapshots immutable prepared values before queuing, then uses the existing media/
SQLite transaction; failure keeps blocks and bytes for retry. The form and dismissal are disabled
during Save. Parent retirement may clear UI drafts while the acknowledged Store task drains its
already detached values. No inference, clipboard mutation or network transmission is started.

`VocabularyImageView` owns its visible bitmap and optional 760x580 enlargement window. It reads
editor draft bytes first, otherwise Store's validated media. A Win32 decoder verifies the bounded
PNG and creates a frozen `BitmapSource` off-thread; no native pointer or stream escapes. Frozen
Freezables support cross-thread handoff ([WPF guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/freezable-objects-overview)).
Loading/unavailable states are local to the row and never overwrite Store's notebook error. The
caption is its accessibility label. Visibility loss/selection replacement cancels the wait, closes
enlargement and drops pixels. Version, visibility and media-token guards reject late publication;
reopening a hidden row starts a fresh read. Unrelated Store snapshots do not change the media token.
The original restore-specific invalidation will connect when backup/restore is ported.

Sixteen native Windows tests use compiled controls, actual WIC, actual SQLite and private temporary
files. They cover draft preview/caption/Cancel, saved bytes + notebook enlargement, stable IDs and
same-hash pruning, duplicate admission/late preparation cancellation, fixed failures, 64 blocks,
per-occurrence byte budget, SQL rollback/retry, parent retirement during Save, lazy reads/late
selection results, hide/reopen, missing media, unsafe/oversized source admission and shared-readable
source files. Gate adapters pause actual native preparation/reads rather than fabricate bytes or
commit results. The production picker callback is exercised; OS dialog interaction itself is not
automated. Existing original-source content/editor/media verifiers remain unchanged; these new
Windows UI cases are source-derived regressions, not new captured Mac UI observations.

The [existing-file drop adapter](VOCABULARY_FILE_DROP.md) now connects Unicode Shell paths to this
import flow. Nonmutating clipboard paste, virtual files and pixel-only drags remain unconnected. Cross-process OLE data extraction is synchronous and can block; editor cancellation must not
wait on a foreign provider. Even file reads check cancellation between native calls and cannot
interrupt an already blocked synchronous read, so a hung network/removable path can delay Store
shutdown draining. Ordinary local-file CI does not establish those external host behaviors, broad
codec/color equivalence, physical desktop interaction or visual pixel equivalence with Mac.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# The Windows CI runner executes these actual WPF/native operations:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyImageUiTests
```
