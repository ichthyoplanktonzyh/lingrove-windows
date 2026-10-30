# Identity-bound atomic image repair at Mac 3b

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Vocabulary/VocabularyImageStorage.swift` (`write`),
`VocabularyArchive+Media.swift`, and
`LingroveTests/Services/Vocabulary/VocabularyContentTests.swift`
(`validBackupRepairsCorruptedMediaAndHasPrivatePermissions`). A validated backup may repair a
damaged regular image at its immutable digest name. Repair is retained even when later notebook
SQL fails or cancellation prevents metadata acknowledgement; only newly created media is removed.

## Windows composition

Both file-backed `Write` and canonical `WriteOwned` now use the same admitted writer in
`WindowsVocabularyImageStorage`. The AppHost-owned Store and its existing exclusive database lock
continue to serialize the entire media/metadata operation. There is no second Store, SQLite
transaction manager, format change, migration or cloud operation.

`WindowsVocabularyImageRepair` is an optional local NTFS effect. It borrows the pinned
`VocabularyMediaDirectory` lease and checks `FILE_SUPPORTS_TRANSACTIONS` before using Windows
KTM/TxF. The ordinary corrupt leaf's verified native file ID is checked again through a transacted
writer handle, with regular-file and private-ACL admission. The handle requests write-attributes
and delete access, shares reads only, marks that exact object for deletion, and closes before commit.
Its transaction retains writer/namespace locks after the ordinary handle closes.

The replacement is created with `CREATE_NEW` and the existing protected current-user ACL, written
and flushed, then renamed through its held handle with no replacement flag. The target deletion
and stage publication commit together. Outside readers see the committed old namespace until
commit; no nontransactional quarantine/publish interval is introduced. Returned identity and bytes
are checked after commit. Native handles use SafeHandle ownership, explicit UTF-16/System32 imports,
one-byte `FILE_DISPOSITION_INFO.BOOLEAN`, and DWORD only for the separate extended disposition.

A valid concurrent winner is reused. A changed corrupt identity, occupied stage, reparse leaf,
unsafe permission, unavailable transaction or conflicting writer fails closed; no pathname
replacement, in-place truncation, retry against another object or nontransactional fallback occurs.
New-file publication still uses the existing no-overwrite path. A corrupt winner arriving after
the initial absent check is preserved and rejected rather than admitted as the earlier object.

The file transaction commits before the existing, separate SQLite transaction. Repair returns no
new-media receipt (`false` for `Write`, `null` for `WriteOwned`), so a later SQL failure/cancellation
retains repaired bytes while graph/revision remain unchanged. New-media receipts still support
identity-bound rollback. Exceptions before file commit roll back the local file transaction;
closing its last handle also provides the Windows process-termination rollback boundary.

## Capability and verification limits

This adds a **local NTFS capability**, not a universal Windows atomic-replace primitive.
Microsoft [recommends avoiding a dependency on TxF](https://learn.microsoft.com/en-us/windows/win32/fileio/deprecation-of-txf)
because it may be unavailable in future Windows versions. Ordinary rename lacks an expected-target
file-ID parameter, and the proposed alternatives do not provide this adapter's verified-target
atomic repair contract. TxF is therefore optional: absence or refusal reports the existing fixed
invalid-content failure and preserves the old file/graph. No service is enabled, DTC enlisted,
filesystem configuration changed or dependency installed. New images and valid-media reuse do
not require this capability.

[TxF programming constraints](https://learn.microsoft.com/en-us/windows/win32/fileio/programming-considerations-for-transacted-fileio-)
include local NTFS, no network transaction, no EFS repair and handle restrictions. Unsupported
volumes, redirected storage and conflicting retained handles may refuse repair. This is an
explicit platform limitation. Transaction timeout zero preserves the source's acknowledged local
write rather than adding an arbitrary deadline. Forced power loss, disk-controller guarantees,
every filesystem/filter driver and every future Windows version are not verified.

Native tests use only marked private temporary roots and synthetic PNGs. They check atomic
visibility and native identity; separate-process write/delete/rename/replace rejection while
the target is closed and just before commit; original identity/bytes after precommit failure;
retained-reader outcomes; stage collision; changed target identity; an unexpected corrupt
new-file winner; writable-handle and reparse rejection; protected ACLs; canonical Store save;
backup restore; and repaired-existing versus new-media rollback after SQL failure/cancellation.
The original shared corpora and their older explicit pins are unchanged.

An additional isolated native feasibility probe on Windows 10 x64/NTFS verified all sixteen
external mutation attempts were rejected, old bytes remained visible before commit, commit and
rollback selected the correct native identity, and forcibly terminating a child before/after
commit respectively retained the original/repaired file without a visible stage. This is an OS
primitive probe, separate from the production Store regressions; it is not a power-loss test.
The focused production regression passed 75/75 (zero skipped); Release solution compilation passed
with zero warnings/errors. Full independent regression and exact final-head CI results are
recorded in the PR.
