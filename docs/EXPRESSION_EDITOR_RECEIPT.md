# Expression editor receipt boundary (in progress)

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/ExpressionAssist/ExpressionAssistEditor.swift`,
`Lingrove/Support/ExpressionAssist/ExpressionAssistFocusPolicy.swift`,
`Lingrove/App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`,
`Lingrove/Services/LanguageLearning/LearningSelectionReplacement.swift`, and
`LingroveTests/Support/ExpressionAssist/ExpressionAssistPolicyTests.swift`.

`ExpressionEditorReceipt` is a request-owned value containing the full editor value, original
UTF-16 selection, exact explicit replacement range/source, process and field identity, focus and
observation versions, and clear composition evidence. Value length is capped at 65,536 UTF-16
units (at most 131,072 bytes of UTF-16 payload); truncated, secure, malformed, unsupported or
unknown-composition inputs fail closed. The exact trigger must precede a collapsed selection.
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
the same gate into every editing owner. A retained-host composition observation, native resume and
single-dispatch replacement are not yet implemented or connected. UIA TextEdit is absent for some
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
