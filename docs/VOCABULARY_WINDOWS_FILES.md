# Windows vocabulary files

This adapter extends the real SQLite transaction library to named Windows files. AppHost, durable Save UI, notebook, media and CLI remain disconnected. The
[archive owner](VOCABULARY_ARCHIVE.md) now composes the adapter with semantic admission and caching. Native file tests run
only on Windows CI; macOS can compile them and run the unchanged in-memory transaction suite.
No desktop/editor, model, installer or downloadable artifact is involved.

The Mac behavior reference remains `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Mac responsibility | Windows implementation |
| --- | --- |
| `VocabularySQLiteConnection`: regular leaf, NOFOLLOW, retained device/inode, serialized native handle | Connection plus `VocabularyWindowsFile`: opened regular/reparse checks, retained handles, volume and 128-bit file ID |
| `VocabularySQLiteFileLock`: bounded shared/exclusive advisory access | `VocabularyWindowsFileLock`: synchronous fail-immediate `LockFileEx` leases with the same 100 × 20 ms retry budget |
| `VocabularySQLiteDatabase.create`: fully initialized closed staging file, exclusive publish | Same-directory stage with protected user ACL; `MoveFileExW` write-through without replacement or cross-volume copy |
| New directory 0700 / database and lock 0600 | Protected current-user directory/file ACLs applied at creation; existing ACLs never rewritten |
| `VocabularySQLiteTests`, `VocabularyArchiveTests` | Existing 20 native Mac memory observations plus Windows-only `VocabularyWindowsFileTests` |

The database owns one connection and one metadata/media lock. It acquires the OS lock and checks
file identity before a complete synchronous operation. Nested shared/shared and exclusive access
reuse the lease; shared-to-write upgrades fail. The connection checks again around transactions.
No statement or lease crosses an await. The memory admission helper rejects named connections,
preventing that test path from bypassing lock composition.

`VocabularyWindowsFile` resolves the parent through a directory handle, opens the leaf with
`OPEN_REPARSE_POINT`, and rejects directories, reparse points and non-disk files. It compares volume
and all 128 file-ID bits through both canonical and original names. Retained read handles share
read/write and omit delete sharing, so the database, lock and containing directory cannot be renamed
while live. A parent alias may resolve normally; retargeting that alias invalidates further access.
This directory retention is a documented Windows strengthening of the Mac identity check.

Native calls use explicit extended paths. SQLite also selects its normal-locking `win32-longpath`
VFS: the default Windows VFS has a separate 1040-byte filename budget, even for an extended path.
The long-path regression exceeds that UTF-8 budget and checks commit/reopen. Alternate streams and
Win32-trimmed leaf names are rejected before any parent/staging mutation.

New parents get a protected current-user inheritable ACL, and every component observed missing is
checked after creation, including a concurrent creator's winner. New database and lock files get
explicit protected user ACLs even in an existing directory. A pinned descriptor is borrowed only
during the synchronous `CreateFileW` call. Existing directories/files keep their chosen ACLs.

SQLite's Windows VFS creates journal/WAL/shared-memory files with default parent inheritance;
the main database's explicit ACL does not propagate to them. Before SQLite opens or recovers a
named database, the adapter therefore validates the actual resolved parent's owner and inheritable
data ACL. Current user, SYSTEM and built-in Administrators are trusted; creator-owner rules resolve
to the new file owner. Null/ambiguous or other-principal inheritable data access fails closed. Unsafe direct directory
grants (including DACL changes, creating or deleting children) also fail, even without inheritance flags.
Existing main/lock and sidecar leaves also require trusted ownership and private data ACLs. The
opened object's ACL is queried through a temporary duplicate handle; the identity handle remains
owned by its connection. Existing sidecar links, directories and unsafe ACLs are rejected unchanged.

This is a necessary platform limit: a directory with broadly inherited data access is unsupported
for the live Windows store. It is not silently secured, nor repaired after data has been written.
The product's private per-user directory is the intended destination. Supporting arbitrary shared
directories would require a security-aware SQLite VFS. Directory/file permissions can still be
changed by their owner or an administrator outside this protocol; this adapter is not a sandbox.

Creation publishes only after schema initialization and closing SQLite. A concurrent existing
destination is validated; it is never replaced. Failures remove only this attempt's random stage
and its sidecars. Existing corruption, unsupported versions, unsafe names/ACLs and uncertain
identity produce fixed recoverable errors without exposing file paths or notebook contents.
WAL and FULL synchronous settings retain source durability policy; tests do not simulate power loss.

The Windows suite contains 37 real filesystem/SQLite cases: persistent reopen, two-connection
revision conflict, incremental payload writes, version-one upgrade, corruption/version preservation,
ACL creation/admission, live and existing sidecars, leaf/parent aliases, retained handles, shared and
exclusive contention, exception cleanup, independent-process locking, concurrent creation and long
Unicode paths. The child process uses Windows PowerShell's FileStream byte-range lock in its own
temporary folder and exits under explicit timeouts. Tests have no fallback or skipped native cases.
The existing fifteen Swift verifiers retain their original expectations; new platform cases do not
change the Mac oracle.

Official API/source references: [CreateFileW sharing and reparse rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
[FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info),
[LockFileEx](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-lockfileex),
[MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw),
[SQLite 3.53.4 Windows VFS](https://github.com/sqlite/sqlite/blob/version-3.53.4/src/os_win.c).

```sh
# macOS: compilation and memory tests only
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
# Windows: actual native file tests, also included in the ordinary Inference CI suite
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyWindowsFileTests
```

These checks establish a bounded file/transaction adapter. The separate archive now supplies semantic admission and cache/revision acknowledgement;
AppHost publication, media lifetime and user-visible Save/notebook work remain open. They
also do not establish native UIA, IME, clipboard acknowledgement, ghost geometry or GPU behavior.

All 37 native file cases passed on Windows x64 at the final PR34 head
`803d543a96a064636b4cd876241b702d8c7a6bfc`: [CI run 39912053197](https://github.com/ichthyoplanktonzyh/lingrove-windows/actions/runs/39912053197).
