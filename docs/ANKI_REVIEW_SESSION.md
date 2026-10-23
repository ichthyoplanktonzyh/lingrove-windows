# Review preparation and send session

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. This is an adaptation of the stable
Mac session, not a replacement review workflow. It is a library slice; no preparation window,
notebook Send command, batch session or AppHost speech provider is connected yet.

| Pinned Mac source | Windows responsibility |
| --- | --- |
| `Cotabby/Services/Review/AnkiReviewSession.swift` | `App/Review/AnkiReviewSession.cs`: one window's draft, acknowledged baseline, mapping, validation, save/CAS and binding choice |
| Same source: `load`, `updateTemplate` | `.Metadata.cs`: replaceable metadata reads and guarded publication |
| Same source: `send`, `synchronize`, `remoteNote`, `resolve`, `write` | `.Sending.cs`: controller lease, captured operation scope, pending identity, conflict choices, remote verification and durable receipt |
| Same source: `prepareAudio`, `uploadAudio` | `.Audio.cs`: explicit audio intent, temporary preparation and immediately saved media receipts |
| `Cotabby/Services/Review/AnkiReviewSession+Images.swift` | `.Images.cs`: live template checks and guarded image reads/uploads before note writes |
| `CotabbyTests/Services/Review/AnkiReviewTests.swift`, `AnkiReviewImageTests.swift` | Complete-source flow observations plus Windows native lifetime/reopen checks |

The shared controller lends its Store, settings, transport and optional `ISpeechExporting`. The last
optional constructor argument preserves existing callers; a legacy explicit speech-configuration
identity callback still takes precedence. A session owns no transport or archive. Calls belong to
one UI context, matching MainActor; awaits retain that context. A future sheet must cancel its tasks
and discard/stop its audio preview when released. It must borrow the existing controller and Store.
The five file parts keep draft, replaceable reads, transaction ordering and media sequencing legible
without introducing separate mutable owners. `FoundationText` exposes the existing exact Foundation
whitespace rule across the Core/App boundary for blank card-name handling.

The initial plan assignment bypasses Swift's observer. Its final non-preset recognition mutation
invokes the wrapped-property observer and synchronizes layout audio kinds; native observations caught
this subtle distinction. Subsequent plan edits synchronize audio kinds from the layout. Save compares
the current review with the acknowledged baseline, retains a pending binding's identity while its
deck changes, and clears imported drafts only after Store acknowledgement. Windows passes immutable
`VocabularyPreparedImage` values through its existing archive contract, where Mac passes validated
PNG bytes. These are still transient drafts, not independent media persistence owners.

Send captures the original connection/model/mapping/speech scope and uses the controller's operation
lease. It rechecks source material and scope across suspension points, uploads media before note
writes, saves a pending binding before any add, searches its explicit original identity on retries,
and validates the returned note before recording a receipt. Merging a sense retains that original
identity. Remote edits require explicit per-field choices; preserving remote text creates review-only
overrides. A missing note requires explicit recreation. Confirmed note updates retain their note ID.
Defaults are remembered only when both the window's initial values and Send's snapshot still agree
with current settings; unrelated edits before the click or during an await survive.

Cancellation follows the actual storage boundary. Although the Swift Store signature has no token,
its task cancellation reaches `Archive+Media.stageImagesAndCommit`, which checks before the atomic
SQL commit, even without images. C# forwards its explicit token. A cancellation after media/profile
acknowledgement can leave a receipt in the draft but not in SQLite; retry may regenerate media. A
cancellation after note verification still permits the separate receipt transaction, as in the source.
No acknowledged SQL commit is undone and remote media is never deleted on cancellation. The expanded
native trace found and corrected an initial mistaken assumption about this nested cancellation check.

`verify-anki-session-macos-parity.py` compiles the complete original session and image extension,
complete controller/navigation, actual archive/SQLite/media and exercised original Store bodies,
complete card/layout/field/speech policies and original model declarations. The original
`ReviewFakeAnki` actor and `ReviewFakeSpeechExporter` class are included verbatim. An observing wrapper
records calls and supplies deterministic await/cancellation hooks; it does not implement product
rules. Four controller diagnostic fragments (one import and three logger calls) are guarded and omitted for the standalone composition.
The source compiler uses whole-module optimization while preserving source-file boundaries. Early
three-case outputs matched the ordinary optimized compilation before expanding the matrix.

The 57 source-derived extension traces compare every operation outcome, full draft, validation,
metadata, conflict choice, settings, controller status, temporary audio, complete Store/disk graph,
remote notes/media hashes and ordered request parameters. They cover constructor observers/defaults,
repeated add/update, lost replies, merged bindings and identity recovery, competing drafts, remote
conflicts and refreshed choices, protected identity, missing-note recreation, profile/connection and
late draft changes, defaults CAS, translated decks, legacy/custom templates, missing mapped fields,
nested newer metadata completion, audio reuse/force/missing media, cancellation on both sides of
receipt commit, and imported/stored/missing images. The native process pins/asserts en_US for error
snapshots, including the original test server's Foundation URL errors. These are observations from
an isolated executable, not execution of the Swift Testing suites or a real Anki server.

Expectations are captured only from that executable. Corpus IDs are locked by SHA256. Known fixture
UUID text and user text retain exact case. Only generated IDs discovered in domain `id` members are
replaced by stable symbolic identities; the explicit UUID-shaped answer case remains literal.
Distinct runtime dates become symbolic identities, retaining presence, equality and refresh across
sends; absolute time and elapsed duration are outside this projection. Enum-keyed dictionary pairs
and sets are sorted; strings above 2000 UTF8 bytes retain byte count and SHA256 rather than full payload.
The portable C# effect server mirrors original test effects and uses prevalidated detached PNG bytes;
actual session/Store/archive/SQLite code executes. Portable `reopen` reconstructs a session over the
same Store, and is not evidence of process restart or Windows named-file admission.

Two additional Windows-only tests use the compiled app and STA dispatcher. One closes the actual
named archive/Store after a committed lost reply, reopens it, merges senses and recovers the same
remote identity without another add, then reopens again to inspect the receipt. The other holds an
add acknowledgement, checks the shared controller lease, and verifies session/controller notifications
remain on the caller dispatcher after release from a worker. These require Windows CI execution;
a successful cross-build alone does not execute them. Physical desktop interaction, actual Anki,
real synthesis/playback, WIC/Shell behavior and host timing remain separate gates.

Local validation: 57 focused traces, full Core 4418 and portable App 724 tests; Release solution
cross-build with zero warnings/errors. Final-head Windows CI must execute the 2 additional native
cases (expected compiled WPF 508 total), and Mac CI must pass all 59 source verifiers before merge.
The Mac job timeout is 75 minutes because an earlier 51-verifier hosted run hit 50 minutes while its
siblings completed. No verifier or case is skipped to address runner variance. No model download,
installer, downloadable package, artifact upload or external request is added.
