# Existing Anki application activation

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Review/AnkiReviewController.swift` (default `activateAnki` closure) and
`AnkiReviewController+Navigation.swift` (`reviewDeck`). The original controller fixtures keep
their historical references. This module only adapts the existing-app OS effect.

AppHost injects an inert `WindowsAnkiActivator` into the shared Review Feature. The existing
controller invokes it after the deck overview acknowledgement and its current-operation guards.
The adapter discovers up to 64 existing `anki` processes, admits only a visible main HWND owned
by that PID with an accessible `anki.exe` executable in the current Windows session, restores a
minimized window, then rechecks identity before requesting foreground. No title text is read.
It does not start Anki or select another collection. Missing/exited/inaccessible processes are
ignored, as the Mac optional running-application activation is best effort.

Windows may refuse [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).
The adapter respects that decision; it uses no synthetic input or thread-input attachment.
Python-only or differently named executable hosts remain unsupported by this conservative
discovery path. Real installed versions, foreground restrictions and minimized/modal windows
remain part of final physical acceptance; fake-driven tests do not establish those behaviors.

Independent source review covered the adapter, Feature forwarding, AppHost registration and
the existing navigation guard. Windows x64 Release solution compilation passed with zero
warnings/errors. Four injected-effect tests passed, including absent application, minimized
restore ordering, replaced HWND/PID rejection and single-application activation. They perform
no process enumeration, native focus effect or network request.

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~WindowsAnkiActivatorTests'
```
