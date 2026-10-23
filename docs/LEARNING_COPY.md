# Explicit sentence-card Copy actions

This ports `LearningSentenceCard.copyActions` and its `LearningSentenceCardTests.swift` layout
responsibility at Mac commit `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The Mac details footer
offers Copy source text while translating and Copy translation for a complete nonempty translation.
The Mac error branch hides details and its Copy footer; the Windows error card does the same.
`LearningSentenceDictionaryText` intentionally declines selection/first responder. The Windows
sentence card follows this explicit-button flow; selecting the separate explanation text remains
a later platform adaptation.

| Windows boundary | Ownership and data flow |
| --- | --- |
| Core `LearningPreviewCopyRequest` / `LearningPreviewCopyPolicy` | Retains a button's immutable document and intent. Resolves exact text only while request ID, source, captured settings and applicable completion/result agree. Annotation changes do not retire the payload; error/retired documents do. |
| `LearningPreviewWindow.Copy.cs` | Owns document-bound nonfocusable footer buttons and fixed feedback. Equal documents retain buttons across annotations; removed buttons cannot copy a newer document. Hide/capture failure clears receipts. The footer stays outside the bounded 460-DIP body. |
| `LearningPreviewController.Copy.cs` | Checks active capture, intent/revision, direct lifetime token, cached field/settings and coordinator document. Supplies the final grant and marshals still-current feedback to WPF. No text is recaptured for copying. |
| `LearningTextCopyController` | Borrows the app clipboard writer and admits at most one worker attempt. It never blocks WPF on native clipboard IO, queues a retry or constructs editor/runtime services. Disposal closes pending admission without joining an already-admitted OS call. |
| Core `IClipboardTextWriter` / `ClipboardTextCopyPolicy` | Narrow explicit-copy/final-grant contract and a 128-Ki UTF-16-unit safety cap. Empty, embedded-NUL, malformed and oversized payloads reject whole; no whitespace, normalization, line-ending or grapheme transformation occurs. |
| Win32 `ClipboardTextWriter` | Owns prepared `HGLOBAL`, exclusive clipboard access and one Empty/Set/Close transaction. AppHost constructs it independently of translation/runtime and insertion's temporary paste lease. |

The native adapter prepares all moveable zero-terminated UTF-16 memory before opening or emptying
the clipboard. It uses the existing process-owned message-window handle. **After exclusive open,
before EmptyClipboard**, it asks the controller for a final grant under the capture-admission lock.
A retired capture, newer click or changed document cannot acquire this grant. The grant is the
linearization point: once admitted, the in-flight Empty/Set/Close transaction finishes even if the
card retires afterward. No app lock spans native clipboard IO, since ownership notifications may
wait on another process. Feedback is discarded if its originating capture/click has since retired.

Successful SetClipboardData transfers the allocation to Windows. Preparation/open/empty/set
failures dispose untransferred memory; successful opens are closed, and a failed close cannot
report success. The ownership contract follows
[SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata)
and [OpenClipboard](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openclipboard).

Explicit user Copy replaces the clipboard with plain text, as Mac does. It is not restored or
subtracted from visible clipboard user changes. A concurrent insertion lease observes its new
sequence/owner and leaves it alone under the existing lease rules. Busy/failed attempts report a
fixed message and require an explicit new click. If Set fails after Empty, this action cannot
restore the previous content; it reports failure rather than success. An in-flight native call
cannot be interrupted safely, so shutdown revokes pending grants without waiting on external owners.

Portable Core cases cover literal Unicode/whitespace, complete-only translation, document/settings
identity and bounds. Portable App tests suspend a fake native writer across retirement/disposal,
check that callers stay free and a second worker is not queued, and distinguish revocation from
an already-admitted transaction. Fake-backend Win32 cases check grant/prepare/open/empty/set/close
failure ordering without touching the runner's clipboard. Compiled WPF cases check actual footer
layout, events, removed-button rejection, annotation retention, error retirement and feedback.

This is implementation and isolated regression evidence. Physical clicks with another app focused,
actual clipboard contents/conversion, real contention and paste-consumption races still need a
Windows-host walkthrough. No editor replacement, automatic copy, added prompt context, model/network
work, package, artifact upload or release is added.
