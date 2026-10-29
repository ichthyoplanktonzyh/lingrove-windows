# Input composition safety checkpoint

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/App/Coordinators/Suggestion/SuggestionCoordinator+HostMarkedText.swift`,
`SuggestionCoordinator+Prediction.swift`, `SuggestionCoordinator+Acceptance.swift`,
`Lingrove/App/Coordinators/InlineFeatures/LearningSentencePreviewCoordinator.swift`,
`Lingrove/Services/Focus/Resolution/FocusSnapshotResolver.swift`, and
`Lingrove/Support/LanguageLearning/LearningInputMethodPolicy.swift`. Mac holds prediction and
presentation on host marked text and rechecks learning capture; the Windows requirement is more
conservative when host evidence is unavailable.
Related reference tests include `LingroveTests/Support/Input/HostMarkedTextPolicyTests.swift`,
`LingroveTests/App/Coordinators/Suggestion/SuggestionCoordinatorInputTests.swift`, and
`LingroveTests/App/Coordinators/InlineFeatures/LanguageLearningCoordinatorTests.swift`.

`FocusedField` carries Unknown, Composing or Clear and an observation sequence. Only Clear with a
positive sequence is admissible. `InsertionTargetPolicy` requires the same receipt in each fresh
target read, including after paste preparation. `LearningPreviewController` rejects before capture
and retires a pending or active capture on receipt changes. A matching receipt is meaningful only
inside the same field and focus epoch; selection, text and caret checks remain separate.

`UiaTextEditEvidence` is the single MTA owner for TextEdit subscriptions and focus handler
registration. It matches native UIA runtime ID and process to the captured element, requires the
TextEdit pattern and successful separate composition/finalization registrations, and samples active
composition on both sides of the bounded text read. A callback advances the receipt without reading
its payload, and immediately invalidates the tracker's current snapshot. Focus changes, stop,
timeouts and subscription retirement invalidate the epoch. Concurrent older captures cannot
replace a newer one. Pending observations are bounded and timed-out queued work is retired before it
can query another field. Missing patterns, failed calls, changed observations and timed-out reads remain
Unknown. The focus owner never asks a secure element for its text or composition metadata.
After a composition event, the focus owner asynchronously coalesces a fresh capture, guarded by
focus and notification epochs; a finalized composition can therefore publish a new clear snapshot
without waiting for another key or focus event. Handler registration belongs to a start generation,
so a delayed older start cannot register over or stop a newer one.
Retirement commands carry the invalidated epoch, so an older stop cannot retire a newer
subscription. A timed-out or overloaded observation invalidates current work without treating its
own failure as a new composition event; later external events can start another capture.

This implementation depends on the provider honoring TextEdit's reporting contract. It does not
establish that every editor supports or correctly implements that contract. Unsupported editors
remain unavailable. GetActiveComposition describes IME composition; a null range does not prove
the absence of a host-owned inline prediction. Mac also holds for host predictive marked text, so
the Windows observation does not establish complete host-marked-text parity. Completion generation
and key handling require their own coordinator guards; this checkpoint alone does not establish them.

Microsoft's [TextEdit provider contract](https://learn.microsoft.com/en-us/windows/win32/winauto/textedit-control-pattern)
defines active composition and requires change events. Its
[client event API](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation3-addtextedittextchangedeventhandler)
requires separate registrations for change types, and
[removal](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation3-removetextedittextchangedeventhandler)
can race a late callback. The native callback's reference-counted COM wrapper keeps its managed
context alive for late calls, whose retired epoch is ignored. UIA calls are not made while holding
the focus lock; the owner joins its worker on disposal. A hung native provider can therefore delay
shutdown even though individual observations time out. WPF's managed UIA wrapper does not expose
the TextEdit client interface used here; CsWin32 supplies the native declarations. A null or failed
IMM query, an input locale, or missing pattern cannot
establish Clear for TSF or unsupported controls. Provider correctness and physical IME/browser
acceptance remain unverified, even after synthetic tests.

Executed on Windows x64: `dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj
--configuration Release --no-restore` passed 4,741 tests after an initial restore; the focused
insertion filter passed 64. `dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj
--configuration Release --filter 'FullyQualifiedName~KeySenderTests|FullyQualifiedName~Win32InsertionBackendTests'`
passed 8. `dotnet build src/Cotabby.App/Cotabby.App.csproj --configuration Release` passed with
zero warnings/errors. These are policy/build checks, not native IME acceptance.

The native callback and serialized owner tests run without reading the foreground field or typing
into any application. They exercise forced-GC callback retention, retired and post-stop callbacks,
and serialized operation ordering after a bounded wait; they do not simulate a compliant TextEdit
provider or validate real IME events. A separate test verifies queued timed-out requests retire
before any native field query. Physical Windows host, TSF and browser acceptance remains open.
`dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release
--no-restore --filter FullyQualifiedName~UiaFocusCompositionTests` passed 4/4 fake-driven lifecycle,
finalization, delayed-retirement and timeout-recovery tests without focusing or reading any desktop
application.

## Native bootstrap and owned-window probe

On Windows 10 x64 build 19045 with .NET SDK 10.0.403, the original `CUIAutomation` CLSID with
`IID_IUIAutomation3` returned `0x80004002` (`E_NOINTERFACE`), so the producer could never provide
clear evidence on this host. The adapter now requests that interface from the documented
[`CUIAutomation8` class](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/hh448746%28v%3Dvs.85%29);
the MTA owner observed `CoInitializeEx = 0x00000001` and `CoCreateInstance = 0x00000000`, with a
non-null `IUIAutomation3`. Failure still leaves composition Unknown. The older class and newer class
may use different proxy providers, as Microsoft documents; no equivalence across hosts is assumed.

An owned, hidden WPF HWND was examined through
[`ElementFromHandle`](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-obtainingelements)
without taking focus or querying the foreground field. Its native runtime ID agreed with the managed
`AutomationElement.GetRuntimeId` formatting; the IDs are opaque comparisons, not durable identities.
The actual HRESULTs were `0x00000000` for element lookup, pattern lookup and separate composition and
finalization handler registration and removal. The HWND itself returned **no TextEdit pattern**:
successful event registration alone does not prove provider support, receipt delivery, or Clear.
These checks do not establish real editor, IME, TSF, inline prediction or insertion behavior.

An additional model-free owned-control check on the same Windows 10 x64 build 19045 host compared
actual editor controls. A hidden parent owned native `EDIT` and `RICHEDIT50W` child HWNDs and a WPF
child source containing a StackPanel and a real TextBox automation descendant; no window was shown
or activated. Managed and native runtime-ID formatting matched for all three. The UIA element and
pattern lookup HRESULTs were `0x00000000` in every case, but the EDIT and WPF TextBox returned null
TextEdit pattern pointers, so their composition evidence remains Unknown. RichEdit returned a
non-null TextEdit pattern; `GetActiveComposition` returned `0x00000000` and a null range for that
unfocused test control. Composition and finalized event registrations and removals returned
`0x00000000` for each control. This is a point-in-time API capability observation, not proof of
event delivery, actual typing or IME handling. A successful event registration is not proof of
TextEdit support; physical application acceptance remains open.

The native descendant lookup walks at most 64 elements below the owned WPF source; it compares
opaque runtime IDs and reads no field text. The owned RichEdit library and all child HWNDs are
released after the MTA owner is joined. This does not read the user's active field or change focus.
`dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release
--no-restore --filter FullyQualifiedName~UiaOwnedEditorCapabilityTests --logger
'console;verbosity=detailed'` passed 1/1 on this host.

Reference APIs: [creating Rich Edit controls](https://learn.microsoft.com/en-us/windows/win32/controls/create-rich-edit-controls),
[TextEdit provider requirements](https://learn.microsoft.com/en-us/windows/win32/winauto/textedit-control-pattern),
and [GetActiveComposition](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtexteditpattern-getactivecomposition).

Executed `dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration
Release --no-restore --filter FullyQualifiedName~NativeFactoryCreatesAutomation3OnItsMtaOwner
--logger 'console;verbosity=detailed'` (1/1) and `dotnet test
tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --no-restore
--filter FullyQualifiedName~UiaOwnedBootstrapTests --logger 'console;verbosity=detailed'` (1/1).
The reconciliation/insertion filter passed 65 tests; the focused native callback and insertion
adapter filter passed 10. A full `dotnet build Cotabby.sln --configuration Release --no-restore`
passed with zero warnings/errors after the reconciliation and ordering adjustments.
