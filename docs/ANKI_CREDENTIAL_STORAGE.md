# Windows Anki credential storage

Reference: the independent `AnkiCredentialStore` in Mac
`Models/Review/AnkiReviewSettings.swift`, commit `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

`Win32/Security/DpapiAnkiCredentialStore.cs` implements the review-only `IAnkiCredentialStore`.
The future shared review settings owner borrows it for explicit actions. Construction performs no
filesystem or DPAPI operation. This slice does not construct it in AppHost or add connection UI,
HTTP requests or synchronization.

## Semantics and Windows adaptation

Missing means an empty key. Save preserves exact UTF-8, including whitespace, decomposed text,
newlines and embedded NUL. Only an empty string deletes, and deleting an absent item succeeds.
Corrupt protection, malformed decrypted UTF-8 and storage errors are failures, never an empty-key
fallback. Undefined .NET UTF-16 input is rejected before changing a saved item. The fixed Windows
error names credential storage rather than Keychain and carries no inner exception/path/key data.

Mac uses a separate Keychain service/account. Windows uses a separate `anki-credential.bin` under
the app's roaming folder, current-user DPAPI, and stable Anki-specific optional entropy. It cannot
silently read an inference credential encrypted without that entropy. No key enters preferences,
notebook records or a logger. New parent directories and staged files get protected current-user
ACLs at creation; existing parent permissions are left unchanged. Mutable plaintext byte arrays are
cleared after encoding/decryption; the API necessarily returns the immutable key string to its
explicit caller.

Saving first encodes and encrypts in memory. A read-capable directory handle pins the resolved
publication parent. A private CreateNew stage contains ciphertext only, is flushed, and remains
open without sharing through native handle-based rename. The successful rename publishes the
complete item and its private ACL. On failure, native disposition marks only the retained owned
stage for deletion; cleanup never unlinks a random pathname. A CreateNew collision owns nothing
and leaves the colliding file alone. Closing after commit does not turn an acknowledged save into
a failure. This adapts the original atomic item update to Windows files; it does not change the
independent inference credential store's trimmed-key behavior.

## Evidence and limits

Fourteen Windows-native cases run actual DPAPI, ACLs and file operations in disposable temporary
folders. They cover missing/read/delete without directory creation; four exact-text/reopen/replace
round trips; current-user decryptability and independent entropy; private new parent/file ACLs and
unchanged existing parent ACL; corrupt ciphertext and protected invalid UTF-8; invalid UTF-16/null
input; locked read/replace/delete with prior-key retention and stage cleanup; a directory at the
leaf; inference/Anki separation; deterministic stage collision; and a pinned stage followed by an
injected after-flush failure. The production settings'60 original Mac traces remain unchanged.

The full Release solution cross-build passes with zero warnings/errors. Native execution is a
Windows CI requirement; it cannot be established by this macOS host. Expected suites for this
head are Core3719, Inference553, App332 and WPF497, with the existing48 Mac source verifiers.
These checks access no user's real credential path, Anki collection, socket, desktop editor or
model. Production AppHost ownership and interactive settings/send behavior remain separate work;
cross-account recovery, roaming between computers and crash/power-loss recovery are not tested.
