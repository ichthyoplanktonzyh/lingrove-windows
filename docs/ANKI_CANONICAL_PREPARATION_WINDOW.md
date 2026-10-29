# Canonical review preparation window (unconnected)

Reference: `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/UI/Vocabulary/VocabularyReviewSheet.swift`,
`VocabularyReviewAudioSection.swift`, `VocabularyReviewLayoutEditor.swift`, and
`LingroveTests/Services/Review/AnkiReviewTests.swift`, `AnkiReviewImageTests.swift`,
`FlexibleReviewTests.swift`. The frozen tree has no separate preparation UI test.

`CanonicalAnkiReviewWindow` borrows an already opened `CanonicalAnkiReviewSession`, the
app-owned `AnkiReviewFeature.RunAsync` admission delegate, and a configure callback. It owns
no Store, credentials, client, or controller. The caller must open and show it on the UI
thread and use `RetireAsync` when shutting down. Close cancels window work, drains its accepted
operations and previews, then detaches the session. Review settings saves first, closes and
retires, and only then calls configure. Save and Send dismiss only after their true acknowledged
session result. Cancel does not write the draft. Session receipt and retry behavior remain in
`ANKI_CANONICAL_REVIEW_SESSION.md`.

This component handles **only the source's default, non-card-ID preparation** and rejects a
card-ID or layout on entry. Preserved custom layouts also remain unavailable. Frozen current
notebook `ReviewSection` and batch entry points use independent card IDs; **this component is
not a current notebook entry point and must not be connected there**. The independent card
layout/preset/template editor, current review navigation and batch UI are separate work.
AppHost and notebook integration are unchanged.

The form shows the normalized local endpoint, loaded profile, note model, association and deck;
provides example, answer and source inclusion choices, preserved default plans, template upgrade,
explicit remote-field conflict choices and explicit missing-note re-add. A changed connection,
model or mapping requires another metadata read before sending. Audio preparation is unavailable;
an existing default-plan audio request is visible and can be explicitly removed, with no silent
removal by Send. The backend rejects audio before remote mutation. Images already in the selected
sense are read from the shared Store for the native preview and uploaded by the session. There is
no WebBrowser, clipboard, image-import effect or background send in this window.

Native WPF preview adapts the known preset's front/back and recognition/gap hierarchy with literal
text and Store-loaded images. For custom remote templates, unrecognized template versions or
field overrides it shows the exact mapped field values as literal text (which may include HTML),
without executing templates, CSS or scripts. This is not a pixel or complete HTML rendering of
Anki, and custom-template presentation remains incomplete. The window follows the existing
notebook Windows palette, Segoe UI typography, high-contrast mapping, system DPI and native chrome;
a test render of the light 96-DPI default form was inspected. Physical desktop, DPI transitions,
keyboard/screen-reader and actual Anki acceptance remain open.

Windows WPF tests use a real isolated named canonical SQLite/media Store and synthetic Anki;
they cover audio removal, acknowledged save, settings-after-close, image preview and cancel,
explicit conflict and missing-note retry, changed destination and draining a held send. Existing
canonical session tests cover transaction and race guards. No user notebook, Anki server, model,
network request or clipboard is used.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalAnkiReviewWindowTests|FullyQualifiedName~CanonicalAnkiReviewSessionTests'
```
