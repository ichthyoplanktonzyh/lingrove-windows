# Guarded editor insertion

This stage prepares a shared write boundary for autocomplete and future language-learning edits.
It does not implement translation, selection replacement, IME tracking or host acknowledgement.

## Ownership and flow

1. `UiaFocusTracker` produces immutable `FocusedField` snapshots with runtime field identity, focus
   epoch, text/caret and explicit reliable-selection evidence. Unknown evidence is ineligible;
   secure fields publish no captured text. A foreground process/window mismatch discards the read.
2. `SuggestionCoordinator` keeps generation, presentation and correction plans tied to the original
   field. `PendingEditLifetime` cancels queued/active edits on ordinary input, focus, settings or
   dismissal while allowing consecutive accept keys to queue in order.
3. `InlineQueryReplacementPolicy` lets macro/emoji callers authorize only the exact `/query` or
   `:query` inserted at the pre-trigger caret. A same-length query or another caret is insufficient.
4. `GuardedTextInserter` owns one app-wide transaction gate. It validates the complete edit and
   refreshes focus after entering the gate and after preparing paste. Cancellation before native
   dispatch rejects the edit; cleanup still runs after a possible paste.
5. `Win32InsertionBackend` supplies UIA reads, a combined delete/insert `SendInput` batch and a
   clipboard lease. `SendInputTextInserter` keeps existing App callers on that shared boundary.

Output is capped at 64 Ki UTF-16 units and must pass the insertion safety gate. Destructive spans
are currently restricted to printable ASCII, because a Backspace count does not define a portable
Unicode range deletion. Unicode output remains supported. Unsupported replacements fail closed.
Actual autocomplete dispatch requires the exact expected preceding text, caret offset and original
suffix. Display reconciliation can tolerate host publication lag; that tolerance cannot authorize
an edit. A prefix trimmed beyond UIA's 14,000-unit window or a suffix beyond 512 units is explicitly
marked truncated and cannot authorize an edit: repeated windows cannot establish an absolute
document position. Completeness defaults to unknown/ineligible. These long-field snapshots require
a future absolute range anchor. An edit can also be conservatively refused if the host has not yet
published an earlier accept.

## Clipboard contract

Before changing the clipboard, the lease snapshots every enumerated format into private bounded
buffers and preallocates rollback handles. The total cap is 32 MiB / 512 formats. Standard supported
HGLOBAL formats include text, DIB/DIBV5 and HDROP; registered HTML and RTF are allowed. GDI handles,
unknown registered/private formats, unreadable/delayed opaque formats, and oversized snapshots reject
paste before `EmptyClipboard`. An image clipboard containing an unsupported bitmap format therefore
rejects paste even if a DIB representation is also available; this stage does not claim universal
rich-clipboard support.

A real hidden owner window lives for the process lifetime. After a possible paste, restoration
opens the clipboard exclusively and checks the staged sequence and owner HWND before emptying and
restoring the saved formats. An intervening user copy is retained. Own-write accounting covers only
the short exclusive mutations, so a user copy during the lease remains visible as context.

Restoration can still fail because of OS contention or a native allocation/`SetClipboardData`
failure; recovery is best effort. The 80 ms consumption wait is a heuristic. It cannot establish
that a host consumed Ctrl+V before restoration. A partial input batch is uncertain and is never
retried or automatically rolled back against a potentially different editor state.

## Evidence and remaining gate

Core tests exercise identity/epoch/selection rejection, text/caret mismatch, serialized edits,
cancellation, clipboard cleanup, preparation failure and partial dispatch using fake backends.
Coordinator tests exercise stale generation/presentation, corrections and queued accepts. Inline
query and emoji tests cover exact anchoring. Windows adapter tests use fake keyboard/clipboard
boundaries; routine CI excludes the opt-in real clipboard test.

Full `SendInput` acceptance means event dispatch only, not host text acknowledgement. There is an
unavoidable race between the final focus read and input dispatch. Native Windows checks must still
cover Notepad/native edits, browser and Electron fields, Office/mail, focus switches, rich clipboard
formats, concurrent copies, elevated hosts, IME and paste timing. Record host/version/architecture
and resulting editor text before promoting any native parity row to verified.

CI remains build/test only: no model weights, desktop smoke, packages, artifact uploads or releases.
The macOS product and its storage are unchanged by this Windows stage.

Independent local validation (2026-10-22):

- `./.dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release`:
  1,651 passed, none failed.
- `python3 scripts/verify-macos-parity.py --mac-repo /Users/shadow/lingrove`: 17 passed against the
  pinned committed Swift policies.

The opt-in real clipboard reader test is excluded from CI and was not run. It skips existing rich
formats but still restores its saved text without guarding a concurrent user copy; run it only in
an isolated desktop test session. It is not evidence for the production paste lease's concurrency.

Win32 contracts consulted: [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput),
[clipboard operations](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations),
and [UIA endpoint comparison](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.compareendpoints).
