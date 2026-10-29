# Canonical review preparation window

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

This component supports the source's default preparation and the independent card-ID layout
editor described in [the canonical card window](ANKI_CANONICAL_CARD_WINDOW.md). The current
notebook's ReviewSection uses independent card IDs; it never substitutes a default plan. AppHost
must attach the existing Feature to the notebook controller before that section can operate.
Batch review remains unavailable.

The form shows the normalized local endpoint, loaded profile, note model, association and deck;
provides example, answer and source inclusion choices, preserved default plans, template upgrade,
explicit remote-field conflict choices and explicit missing-note re-add. A changed connection,
model or mapping requires another metadata read before sending. Audio preparation is unavailable;
an existing default-plan audio request is visible and can be explicitly removed, with no silent
removal by Send. The backend rejects audio before remote mutation. Images already in the selected
sense are read from the shared Store for the native preview and uploaded by the session. There is
no WebBrowser or background send. The card layout editor supports explicit file-image import; clipboard paste and image drops remain unavailable there.

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
