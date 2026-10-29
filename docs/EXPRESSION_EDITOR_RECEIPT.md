# Expression editor receipt boundary (in progress)

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/ExpressionAssist/ExpressionAssistEditor.swift`,
`Lingrove/Support/ExpressionAssist/ExpressionAssistFocusPolicy.swift`,
`Lingrove/Support/LanguageLearning/EditorTextRangePolicy.swift`,
`Lingrove/App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`,
`Lingrove/Services/LanguageLearning/LearningSelectionReplacement.swift`, and
`LingroveTests/Support/ExpressionAssist/ExpressionAssistPolicyTests.swift`.

`ExpressionEditorReceipt` is a request-owned value containing the full editor value, original
UTF-16 selection, exact explicit replacement range/source, process and field identity, focus and
observation versions, and clear composition evidence. Value length is capped at 65,536 UTF-16
units (at most 131,072 bytes of UTF-16 payload); truncated, secure, malformed, unsupported or
unknown-composition inputs fail closed. The exact trigger must precede a collapsed selection.
Shared `EditorTextRangePolicy` requires both Swift-style character boundary and existing
Foundation-composed-range admission, including the empty-caret rule and CRLF distinction. The
combining/emoji/ZWJ/Indic/CRLF regression cases include source-derived cases; they have not run
through a Swift verifier on this Windows host. The existing Foundation compatibility pin is unchanged.
Receipts are memory-only and their text must not appear in logs.

`ExpressionEditorRoundTrip` describes the explicitly authorized registered panel/host path. It
does not itself obtain OS evidence or permit a write. It rejects another window or lost ownership,
requires two exact reads in the same new focus/composition epoch, and consumes its permission once
before dispatch. The integration owner must call Retire on a changed field, panel dismissal,
superseding request or unexpected focus; the normal insertion policy remains unchanged.

`UiaExpressionSelectionReader` is a bounded, read-only adapter: it checks secure and read-only
metadata before asking for text, demands one actual TextPattern selection, and compares bounded
before/selected/after reads with the full value. UIA can stall and mutate; its caller must run off
the UI thread, bound its wait, and compare fresh field identity, focus and composition evidence on
both sides before accepting its result. The reader has no live application ownership or commit path.
Two hidden owned WPF tests confirm an emoji selection and reject a password and oversized field;
no user editor, input or focus was touched.

The common `InsertionTransactionGate` serializes owners but grants no permission to edit. The
existing guarded inserter still applies its original policy; application composition must inject
the same gate into every editing owner. The existing UIA MTA owner now has a separate request-owned
retained host subscription with composition/finalization and text/selection change events,
generation retirement, bounded waits and fresh
field/password/process/foreground and active-composition checks. It only permits the exact host
window or the registered current-process panel while the caller confirms panel keyboard ownership.
It has no independent global focus listener. `Win32ExpressionEditor` borrows the existing keyboard
hook and focus tracker, revokes unexpected focus/input and retained-host events, and owns a bounded
capture and explicit panel-to-host return. Under the shared insertion gate it recaptures before one
native batch. A verified outcome requires observing the expected full value; an incomplete dispatch,
cancellation after possible dispatch or failed clipboard cleanup is unverified, with no automatic
retry or rollback. Only the original selection may be replaced; trigger cancellation/replacement is
limited to printable ASCII before a collapsed caret. Non-ASCII triggers are rejected before opening
the panel. The panel ownership callback must be a thread-safe, nonblocking snapshot.

This draft is not connected to AppHost and has not yet had independent review. Fake-driven session
tests cover matched host return, one-shot Unicode selection and ASCII marker deletion, changed
value, physical input and same-process HWND switch before dispatch, waiting cancellation, partial
dispatch, and clipboard ownership/cleanup failure. They do not establish physical input or native
write behavior. UIA TextEdit is absent for some
hosts, and an unsupported host remains unavailable. No physical IME or desktop acceptance is claimed.

Windows API references: [TextPattern selection](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection),
[TextPatternRange.Select](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.select),
and [foreground activation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).

Validation from this isolated Windows worktree: `dotnet test
tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --no-restore --filter
'FullyQualifiedName~Cotabby.Core.Tests.Insertion|FullyQualifiedName~ExpressionEditor'` passed 74/74;
`dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release
--no-restore --filter FullyQualifiedName~UiaExpressionSelectionReaderTests` passed 2/2. Neither
test establishes native write behavior or physical focus/IME support.

`UiaRetainedCompositionTests` exercises real registration, active-composition query, native callback
lifetime and retirement against an owned hidden RichEdit. Setting test-only text in that control
raised a real UIA text change and retired its receipt; the separate synthetic composition callback
is not a real IME event. Asynchronous provider events and physical IME behavior remain unverified.
`UiaTextEditEvidenceTests.TimedOutRetainedRegistrationIsRetiredBeforeNativeFocusQuery`
exercises a held owner and ensures expired queued registration cannot later query the active field.

The product uses the existing UIA focus event and keyboard hook, not a second global hook. A single
raw focus transition into the registered panel and a single explicit return to the original host
are allowed; any additional focus transition retires the receipt, even if text later reverts. This
is deliberately conservative and may reject hosts that produce additional focus notifications.
The one initiating key-up is exempt only during capture and only for its exact virtual key; no
other physical input is ignored. UIA events are asynchronous and providers can be defective, so a
small race between final observation and SendInput remains and physical testing is still required.

`dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release
--no-restore --filter FullyQualifiedName~ExpressionEditorSessionTests` passed 10/10 with fake native
effects. These do not type into or focus any user application.
