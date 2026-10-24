# Serial review batch session

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Review/AnkiReviewBatchSession.swift` and
`Cotabby/Models/Review/ReviewBatchModels.swift`. This adapts the existing Mac workflow. It adds no
batch window, notebook command, AppHost owner, speech provider or new network destination.

`Core/Review/ReviewBatchModels.cs` owns immutable window-local row snapshots and the nine original
status labels. A row ID identifies presentation only; its frozen `VocabularySelection` and optional
card ID resolve durable Store material. Record replacements retain the row ID. They never replace
notebook or remote note identity. `App/Review/AnkiReviewBatchSession.cs` owns one window's queue,
preparation baselines and cancellable serial task. It borrows the existing controller and rebuilds
an `AnkiReviewSession` for every attempt; it contains no second Anki protocol or storage implementation.

Construction captures the immutable entry graph and preserves first-occurrence selection order.
The entry and sense are independently optional: an unavailable sense can still show its entry's
expression, as the native oracle established. Flexible mode expands existing cards in their stored
order and counts selected senses without cards. Legacy synchronized rows begin synced; rows with
saved audio begin in Verify audio so a new batch checks remote media even when fields match locally.
Resolve follows merged aliases while retaining the original selection in the row. Run deduplicates
by canonical sense ID or card ID; two aliases cannot create duplicate notes.

Start owns one task and ignores repeated Start/Apply while busy. It captures the original operation
scope/defaults, resolves each row from current durable state, skips synced/skipped rows, and borrows
the original single-session load/validation/send transaction. A failed or conflicting row does not
stop unrelated rows. Successful rows remain excluded from ordinary retry. Cancellation retains
successful notes and receipts and marks remaining eligible rows stopped; it performs no rollback.
Each attempted send releases its temporary audio preview/cache. Defaults changed elsewhere during
an await survive, while unprepared rows share the batch's captured deck.

Preparation records the source sense/expression before opening a single draft. Refresh preserves
an unchanged row's current status, including failure feedback; changed material updates its labels
and synchronization state. Apply retains confirmed note decks, uses the exact original integer
card/audio options, and skips those legacy options for flexible card rows. It saves local plans
without sending notes. The source's unknown positive option fallbacks are covered rather than
silently replacing them with different validation.

All calls belong to one UI context, matching MainActor. Immutable row-array replacement protects
retained presentation snapshots but does not make arbitrary-thread mutations safe. The .NET task
adapter reserves `Work` behind a TCS gate before the initial Busy notification and before execution;
`Task.Yield` alone cannot enforce assignment order without a UI context. `StopAsync` requests
cancellation and observes callback completion. A future sheet must also join its captured `Work`
before releasing the controller/Store; cancellation completion alone is not transaction completion.

`verify-anki-batch-macos-parity.py` imports the existing single-session reference composition,
then compiles the complete original batch class and model directly. Actual session/controller,
Store/archive/SQLite/media and domain policies execute; the original Anki/speech test fakes provide
external effects only. It runs in an isolated private temporary archive and en_US preference suite.
It does not execute the original Swift Testing suites or contact real Anki. Expectations were
captured only from that executable, then independently reverified. The 34 locked source-derived
extension cases include empty/unavailable/duplicate selections, frozen order, serial retries,
failed/lost acknowledgements, only-one-row runs, merged aliases, changed source/default/profile,
preparation refresh, exact Apply options, multiple flexible cards, saved-audio verification/repair,
cancellation during generation and cancellation after the first acknowledged note.

Every step compares complete rows/status labels/resolution, Busy/Work/message/counts, settings,
full Store/disk graphs, remote notes/media, ordered calls, export counts and controller failures.
The normalizer is shared with the single-session verifier: runtime UUIDs/dates retain symbolic
identity/equality/refresh, dictionary/set order is canonical, and long payloads retain byte count
and SHA256. Known fixture UUIDs and literal user text remain exact. Absolute elapsed time and
platform media/device behavior are outside these projections. C# consumes the same observations
and actual portable archive over memory SQLite. Its serial test context passes a cooperative
deadline into waits, then stops/joins the batch and disposes Store before closing its queue; timing
out an outer waiter cannot detach the still-running UI callback. The speech effect fake is shared
with the single-session tests without changing its original numbered Float32/WAV behavior.

An additional Windows-only compiled-app test runs on the actual STA dispatcher. During the initial
Busy notification it checks the Work handle is already visible and requests stop synchronously;
it then joins cancellation/work and checks no Anki action started, all rows stopped, and the shared
lease/failures are clear. It drains accepted work even on assertion failure. A cross-build alone
cannot execute this Windows case. No batch UI, actual user desktop, real Anki, audible synthesis,
Shell interaction, model execution or platform-host parity is claimed.

Local checks pass: 34 source observations, 34 portable focused cases, the full 4,420 Core and 758 App
tests, and the Release solution cross-build (zero warnings/errors). Both final-head CI jobs remain
required, including 60 original-source verifiers and the additional Windows case. No installer,
downloadable package, model download, artifact upload, user-notebook write or external request is added.
