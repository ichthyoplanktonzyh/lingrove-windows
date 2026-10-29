# Private WebView profile directory owner

`WindowsPrivateProfileDirectory` is a Windows resource boundary for an explicitly opened
dictionary article. It accepts an existing private parent, resolves and pins its physical drive
root and every ancestor with handles that deny deletion, and atomically creates a unique child
with `NtCreateFile(FILE_CREATE | FILE_DIRECTORY_FILE)`. Its identity, directory type and protected
current-user ACL are verified through the creation handle. Before WebView use, a read-only
deny-delete pin and a second read-only share-delete identity handle are verified against it.
The returned path is the canonical drive path.
Construction does not start WebView, read a dictionary, or touch an existing profile.
`CreateWithPrivateParent` may create a missing private parent using the existing Windows directory
primitive; it checks an existing parent's ACL without modifying it. If setup fails after creating
the leaf, handle-bound cleanup is attempted; an unverifiable or locked leaf is left in place.

The caller must first stop and join every WebView user of this profile. `DeleteOwnedAsync` keeps
the identity handle alive, releases the deny-delete pin, and opens a DELETE-capable deny-delete
handle relative to the pinned parent. It compares the volume and complete 128-bit ID to the still
open identity handle before any deletion; a swapped name fails, and the original ID cannot be
recycled while its handle remains open. It then enumerates through handles rooted in the pinned parent, records each 128-bit file ID, and opens
each child relative to that parent without following reparse points. It compares the opened ID
with the observation and marks the same handle for deletion. Directory links are removed as
links and never traversed. Each retry retains prior name/ID observations, so a replaced child is
not silently adopted after a failed attempt. Cancellation, a lock, access denial, an identity
change or an unsupported filesystem operation fails closed. Some already deleted children may
remain deleted; the original root identity stays held for retry, even if its name was exchanged
during the upgrade gap. Dispose only after successful
cleanup, or deliberately abandon the undeleted directory without claiming cleanup.
Concurrent disposal calls join the same completion, including the in-flight cleanup and closure of
every owned handle. An ancestor handle is registered for cleanup before validation can fail.

The Windows adapter admits at most 100,000 observed entries, depth 64, 255 UTF-16 code units per
component and 64 MiB of retained relative names. Exceeding a bound preserves the root. Ordinary
opens without delete sharing and cache-child rename work while the WebView-use pin is held.
Actual WebView2 compatibility with the complete ownership contract remains an activation
gate and must be checked with a real isolated native WebView lifecycle, not inferred from unit tests.
The owner does not implement startup scavenging, cloud access or old-profile migration.

Microsoft references: [NtCreateFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile),
[FILE_ID_EXTD_DIR_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_extd_dir_info), and
[file disposition](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex).
Native tests use only random temporary directories and synthetic files; they cover nested cleanup,
private ACL, pinned root/ancestors, same-name file and nonempty-directory replacement, link targets,
sharing locks, denied access, cancellation and retry. No user profile or clipboard is accessed.
