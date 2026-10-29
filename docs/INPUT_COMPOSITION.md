# Input composition safety checkpoint

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/App/Coordinators/Suggestion/SuggestionCoordinator+HostMarkedText.swift`,
`SuggestionCoordinator+Prediction.swift`, `SuggestionCoordinator+Acceptance.swift`,
`Lingrove/App/Coordinators/InlineFeatures/LearningSentencePreviewCoordinator.swift`,
`Lingrove/Services/Focus/Resolution/FocusSnapshotResolver.swift`, and
`Lingrove/Support/LanguageLearning/LearningInputMethodPolicy.swift`. Mac holds prediction and
presentation on host marked text and rechecks learning capture; the Windows requirement is more
conservative when host evidence is unavailable.

`FocusedField` carries Unknown, Composing or Clear and an observation sequence. Only Clear with a
positive sequence is admissible. `InsertionTargetPolicy` requires the same receipt in each fresh
target read, including after paste preparation. `LearningPreviewController` rejects before capture
and retires a pending or active capture on receipt changes. A matching receipt is meaningful only
inside the same field and focus epoch; selection, text and caret checks remain separate.

**This checkpoint does not provide a native evidence producer.** Existing UIA snapshots therefore
remain Unknown and these guarded operations fail closed. This is a functional limitation, not a
claim of complete parity. Completion generation and key handling require their own coordinator
guards; this checkpoint alone does not establish them.

Microsoft's [TextEdit provider contract](https://learn.microsoft.com/en-us/windows/win32/winauto/textedit-control-pattern)
defines active composition and requires change events. Its
[client event API](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation3-addtextedittextchangedeventhandler)
requires separate registrations for change types, and
[removal](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation3-removetextedittextchangedeventhandler)
can race a late callback. A future producer must subscribe before querying, serialize add/remove,
invalidate receipts on composition and finalization, reject stale query results and retire focus
epochs. It must not log or retain event strings. WPF's managed UIA wrapper does not expose the
TextEdit client interface used here; native COM integration and its lifetime need independent
implementation and tests. A null or failed IMM query, an input locale, or missing pattern cannot
establish Clear for TSF or unsupported controls. Provider correctness and physical IME/browser
acceptance remain unverified, even after synthetic tests.

Executed on Windows x64: `dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj
--configuration Release --no-restore` passed 4,741 tests after an initial restore; the focused
insertion filter passed 64. `dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj
--configuration Release --filter 'FullyQualifiedName~KeySenderTests|FullyQualifiedName~Win32InsertionBackendTests'`
passed 8. `dotnet build src/Cotabby.App/Cotabby.App.csproj --configuration Release` passed with
zero warnings/errors. These are policy/build checks, not native IME acceptance.
