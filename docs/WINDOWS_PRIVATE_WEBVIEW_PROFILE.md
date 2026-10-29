# Private WebView profile directory owner

`WindowsPrivateProfileDirectory` is a Windows resource boundary for an explicitly opened
dictionary article. It accepts an existing private parent, resolves and pins its physical drive
root and every ancestor with handles that deny deletion, and atomically creates a unique child
with `NtCreateFile(FILE_CREATE | FILE_DIRECTORY_FILE)`. The returned directory handle itself
has delete access and denies deletion sharing; its identity, directory type and protected
current-user ACL are verified through that handle. The returned path is the canonical drive path.
Construction does not start WebView, read a dictionary, or touch an existing profile.

The caller must first stop and join every WebView user of this profile. `DeleteOwnedAsync` then
enumerates through handles rooted in the pinned parent, records each 128-bit file ID, and opens
each child relative to that parent without following reparse points. It compares the opened ID
with the observation and marks the same handle for deletion. Directory links are removed as
links and never traversed. Each retry retains prior name/ID observations, so a replaced child is
not silently adopted after a failed attempt. Cancellation, a lock, access denial, an identity
change or an unsupported filesystem operation fails closed. Some already deleted children may
remain deleted; the profile root and its owner stay held for retry. Dispose only after successful
cleanup, or deliberately abandon the undeleted directory without claiming cleanup.

The Windows adapter admits at most 100,000 observed entries, depth 64, 255 UTF-16 code units per
component and 64 MiB of retained relative names. Exceeding a bound preserves the root. The root
handle requests DELETE while denying deletion sharing: any other handle opened on the root must
itself share DELETE. Actual WebView2 compatibility with this sharing contract remains an activation
gate and must be checked with a real isolated native WebView lifecycle, not inferred from unit tests.
The owner does not implement startup scavenging, cloud access or old-profile migration.

Microsoft references: [NtCreateFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile),
[FILE_ID_EXTD_DIR_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_extd_dir_info), and
[file disposition](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex).
Native tests use only random temporary directories and synthetic files; they cover nested cleanup,
private ACL, pinned root/ancestors, same-name file and nonempty-directory replacement, link targets,
sharing locks, denied access, cancellation and retry. No user profile or clipboard is accessed.
