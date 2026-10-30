# Review and Anki failure presentation

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Review/AnkiConnectClient.swift`, `AnkiReviewController.swift`,
`AnkiReviewController+Navigation.swift`, `AnkiReviewSession.swift`, `AnkiReviewSession+Images.swift`,
`AnkiReviewBatchSession.swift`, and the original `AnkiReviewPolicy`, `ReviewCardPolicy`,
`ReviewLayoutPolicy` and `ReviewBatchModels` under Support/Models. The source localizes owned
messages before presentation; remote/model/deck/profile/user values keep their original roles.
Historical corpus references and expected English observations remain unchanged.

## Boundary and active composition

The finite audit found 52 active status/failure keys: 44 already in the original Mac catalogs,
four already in the Windows supplement, and four missing Windows-specific prompts. This work
connects that inventory through the existing single review Feature/controller, canonical sessions,
batch and settings windows. It adds no Store, HTTP client, credential read, automatic connection,
remote operation or new task lifetime.

`AnkiReviewException` retains its existing string constructor for unclassified diagnostics.
An additive enum constructor identifies only owned failure categories. The original English
Message remains identical; missing card content carries its existing `ReviewContentSource` enum
separately rather than concatenating an opaque string key. Only the presentation adapter translates
those categories and the closed source-role label. An unclassified exception whose text happens
to match a catalog key is still literal.

Core policies and Windows transport/credential failures now attach this metadata at their owned
throw sites. Wire/HTML bytes, error decisions, budgets, IDs, API fields, template names, credentials,
DPAPI entropy/ciphertext and file effects are unchanged. The Anki wire's original oversized-request
message intentionally reuses the speech audio key and its Learning table, matching Mac.

AppHost injects optional fixed-text/typed-error formatters into the same Review Feature. Portable
constructors retain their English defaults. Canonical and legacy session adapters borrow this
controller for validation and failure display; UI catches adapt typed values before losing their
category. Unknown Anki/client exceptions remain literal. The successful connection caption is
translated at its fixed producer. Settings key errors use that same controller formatter.
Home/Welcome's isolated sample Review Feature receives the same formatters; it still borrows only
its private sample Store and in-memory Anki effects. Independent review found that second active
composition, and its missing callbacks were added without changing its sample data or lifetime.

`CanonicalStatus` and its English `Synced` identifier remain unchanged because batch decisions
compare it internally; existing views translate its display value. Batch enum status, retries,
conflict choices, profile checks, note acknowledgement and cancellation are not translated or
altered. Only a new or empty card-name fallback receives the original Mac localized `New card`;
nonempty saved/user names remain literal. The first-use readiness view also adapts its already
typed SpeechException instead of showing its fixed English message.

The original three Mac catalogs and all previous 518 Windows entries are preserved. Four authored
keys cover all fifteen interface locales, bringing the Windows supplement to **522**. Profile,
deck, field, endpoint and secret values never become translation keys.

## Validation and scope

The complete portable Core suite passed **5,157/5,157** and portable App suite **1,278/1,278**,
zero failures/skips, with the new metadata and default English behavior. The focused WPF run
passed **64/64**, including new tests for all fourteen non-English interface locales, unclassified
messages equal to catalog keys, separate missing-source roles, inert composition, localized
connection/address/key errors and literal profile/provider detail. The fake connection uses no
socket. Fourteen actual isolated Windows DPAPI cases passed with an additional assertion that
the existing fixed, inner-error-free failure has the credential category.

The first solution build caught a missing localization namespace qualification in AppHost; its
explicit qualification was corrected before the focused WPF build. This was a compile-only defect;
no native or user-data action was involved. Final review/full WPF/final-head CI results are recorded
in the PR. Existing Mac fixture inputs/expectations were not rewritten for localization.

Native Anki, real credentials, endpoint use, actual audio, physical Windows host, screen-reader,
RTL/mixed-DPI and manual interface quality remain separate acceptance. The settings-only fallback
without either notebook owner retains its earlier unavailable-state scope; production passes the
canonical Store. This presentation pass does not establish automatic language confidence, an
Apple dictionary counterpart or corrupt-existing-image repair.
