# Atomic vocabulary export saving

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/Services/Vocabulary/VocabularyArchive+Export.swift` (`writeExport`),
`VocabularyArchive+Media.swift` (`validateBackupDestination`, `sameFile`), and
`VocabularyArchive.swift` (`writeAtomically`). This adapts existing local file behavior.

`VocabularyArchive.ExportWrite.cs` adds `WriteExportAsync` to the serialized archive worker. It
checks cancellation, admits the user-offered destination before encoding, chooses captured plain
bytes or stored ZIP from `IncludesImages`, enforces the 192 MiB outer-file ceiling and delegates
publication. It never reloads the current notebook or media. The existing lifetime owner admits,
serializes and drains this whole operation; a closed archive rejects it before file effects.
The production constructor retains the notebook path so export cannot overwrite its own storage.
No notebook connection is opened merely to save an already prepared package.

`WindowsVocabularyExportFile` owns one short-lived directory handle and the destination checks.
It resolves the offered parent through a handle, pins directory identity without delete sharing,
and rechecks the offered parent after staging. Existing destinations must be regular disk leaves;
Windows reparse leaves are rejected. The resolved path cannot equal the database, `-wal`, `-shm`,
`-journal` or `.lock`; volume plus 128-bit file ID also rejects hard-link aliases to those files.
The media root and descendants are protected after resolution, including an ancestor alias into
that root. A sibling with a similar name is still a valid export folder.

The user-selected folder need not satisfy SQLite's private-parent inheritance rule. A new random
same-directory stage instead receives the existing protected current-user file ACL at `CREATE_NEW`,
before writing its first byte. The worker writes the complete captured data, calls `Flush(true)`,
keeps its non-delete-sharing stage handle open, repeats destination protection and checks cancellation
before `SetFileInformationByHandle(FileRenameInfo)` replaces the admitted leaf relative to the pinned
parent. FullControl includes DELETE access for that handle-based rename. Failure cleanup marks the
same stage handle with `FileDispositionInfo`; there is no pathname-based rename/deletion or cross-volume
copy/delete fallback. A peer cannot swap the stage between fsync, publication and cleanup. No fallible action
or cancellation check follows successful publication, preventing an acknowledged save being
reported as cancelled after its destination has changed.

Windows API references: [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
for metadata/sharing/reparse/creation semantics, and
[SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
with [FILE_RENAME_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)
for handle-based relative rename and disposition cleanup. This adapts Mac inode/0600/fsync/rename to Windows
file IDs, protected ACLs, directory leases and native rename. It deliberately rejects ambiguous
Windows leaves (ADS/trailing dot or space) and reparse leaves, including links unrelated to notebook
storage; these unsupported aliases are explicit platform differences. Normal existing export files
can be replaced, and their former broad ACL does not leak into the new private file. FAT/remote
filesystem durability and failure behavior are not established by the NTFS CI cases; there is no
fallback that silently drops the private-file creation requirement.

Nineteen shared fixtures execute the complete original Mac save/destination/atomic-writing bodies
in disposable private temporary folders. They cover TXT/CSV/JSON new files, replacement, ZIP with
empty/attached media, disabled-image map gating, bad ZIP paths, all five protected storage names,
media root/child, hard/symbolic links and cancellation precedence. They compare actual destination
bytes, protected-file preservation and absence of owned stages. Protected files are literal dummy
bytes, proving this save path does not open or modify SQLite; these are file-writing observations,
not database-admission tests. Opaque ZIP attachment bytes make no PNG admission claim.

Windows CI runs those same nineteen cases with real file IDs/ACLs/renames, plus 30 native adapter
regressions for metadata and aliases, ACL replacement, collision ownership, prewrite/postflush
cancellation, rename failure, final target revalidation, parent pinning, alias retargeting and denied peer stage replacement/deletion before commit or cancellation.
Four more archive regressions save a prepared TXT/CSV/JSON after actual memory-notebook deletion
and reject saving after archive close. The narrow identity/precommit callbacks control timing only;
all read/write/flush/rename operations remain actual Windows effects.

```sh
python3 scripts/verify-vocabulary-export-save-macos-parity.py --mac-repo /path/to/mac-repo
# Native Windows runner only:
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyExportSaveTests|FullyQualifiedName~VocabularyExportFileTests'
```

macOS can cross-compile these Windows tests but cannot execute them. Final-head Windows CI must
establish actual file-test success. The public export command, Store callbacks, save-session
staleness, picker and export window remain work, alongside full backup/restore. Native desktop
UIA/IME/ghost/insertion and model/GPU gates remain open.
