# Canonical batch review

Reference: macOS `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Review/AnkiReviewBatchSession.swift`, `Lingrove/Models/Review/ReviewBatchModels.swift`,
`Lingrove/UI/Vocabulary/VocabularyReviewBatchSheet.swift`, `VocabularyNotebookView.swift`, and
`LingroveTests/Services/Review/AnkiReviewTests.swift` / `FlexibleReviewTests.swift`. The older
`ANKI_REVIEW_BATCH.md` keeps its original reference and describes a separate legacy library.

`CanonicalAnkiReviewBatchSession` freezes the selected sense IDs, expands every existing independent
card in stored order, counts selected senses with no cards and keeps row IDs for the sheet lifetime.
Each row resolves fresh canonical Store state, including merged-sense aliases, before opening the
existing single-card session. One serial task captures the operation scope, checks it across awaits,
skips acknowledged rows and uses the same controller/client, Store and media transaction. A failed
or conflicting item does not stop later items; explicit retry excludes previously synced cards.
Stop cancels remaining work without rolling back a remote note; the existing pending identity and
receipt transaction permit safe retry after a lost reply. A verified receipt remains committed even
when cancellation arrives. Speech remains unavailable and an audio slot blocks the row until the
user removes it in preparation; it is never silently omitted.

The modal window displays the actual local endpoint and loaded profile before explicit Send; it
rechecks the displayed scope against the current profile when starting and requires refresh after a
change. Its per-row Prepare opens the existing independent card editor, including conflict decisions
and explicit missing-note recreation. Preparation changes update the row; dismissing an unchanged
sheet retains failure status. Applying a deck saves each local card plan and preserves decks already
bound to a remote note. Close/Stop and notebook retirement join admitted work, child preparation and
Store publication before releasing shared owners. The notebook partial accepts a captured selection
through `OpenReviewBatch`; the Review Feature and one canonical Store remain app-owned.

The WPF form uses the notebook palette and native window chrome. A 96-DPI light, dark and simulated
high-contrast offscreen render is optional via `LINGROVE_TEST_RENDER_DIR`; physical desktop,
keyboard/screen-reader, real Anki and network acceptance are still open. Tests run isolated named
canonical SQLite/media and fake Anki only. They cover multiple cards, frozen/deduplicated rows,
merged aliases, partial failure and single-row retry, pending identity after lost reply, explicit
missing-note recreation, cancellation before and after remote acknowledgement, audio rejection,
local deck application, per-row preparation, changed profile and accepted-work drain.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalAnkiReviewBatchTests'
```
