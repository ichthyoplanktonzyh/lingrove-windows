# Notebook explanation and dictionary session library

This slice adapts the stable Mac notebook's target and work ownership. It is a library in App,
now connected to the notebook window by [contextual explanation rows](VOCABULARY_EXPLANATION_UI.md); dictionary UI follows separately. Browsing must restore saved explanations without
starting generation; each saved example and the optional standalone meaning keep separate child
sessions. Explicit dictionary lookup has no explanation service and cannot implicitly start AI.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac responsibility | Windows mapping |
| --- | --- |
| `Services/Vocabulary/VocabularyNotebookSession.swift`: selected entry/sense, per-usage children, activate/cancel, explicit dictionary | `App/Vocabulary/VocabularyNotebookSession.cs`, window-owned dispatcher lifetime |
| Same source: alias/fallback sense, uppercase UUID keys/document identity, dictionary form/kind/ranges | `Core/Vocabulary/VocabularyNotebookTargetPolicy.cs`, pure projections |
| `Services/LanguageLearning/LearningExplanationSession.swift`: saved-first target, generation/stop/refresh | Existing `Core/LanguageLearning/LearningExplanationSession.cs`, with batch revocation before owner transitions |
| `Services/LanguageLearning/LearningDictionarySession.swift`: query/close/resume behavior | Existing Core bundled-only dictionary session; no explainer supplied |
| `Support/LanguageLearning/LearningUnitPolicy.swift`: native word occurrences | Borrowed `ILearningLexicalText`; actual Windows provider does native segmentation off the UI thread |

The app session borrows the shared explainer, dictionary provider and text adapter. It owns child
sessions and a dictionary session, with no Store, editor, runtime, media or network ownership.
Selecting the same entry/sense reuses rows by saved usage UUID and keeps generation when the exact
effective input is unchanged. Changing context resets that child; changing sense or removing a row
retires and unsubscribes it. Saved records must match both input and configured response language.
Activating one row stops the other rows. It does not itself generate.

Mac serializes these transitions on MainActor. Windows child workers can complete while selection
is inside a saved-record or event callback. The owner therefore first revokes affected children
without invoking callbacks. Core keeps their generation and saved-restoration admission suspended
until SetTarget or Cancel completes the transition. A revision rejects old replies even after an
equal target returns. Equal-input rows are not revoked. C# strong service-event subscriptions are
removed when children are retired. UI consumers must still marshal notifications to their dispatcher
and validate row/session identity and the rendered state before acting.

Dictionary preparation uses a weak owner and a cancellation/version receipt. A new query clears
the previous article while native word segmentation runs. Repeated and reentrant calls deduplicate
both pending preparation and an already-open query for that entry. Cancel closes the dictionary but
remembers an explicitly opened entry for Resume; Hide clears that memory. An empty-form expression edit also retires pending preparation when its effective query changes;
an already-open dictionary retains the original Mac form/language invalidation rule. Word-provider failures
show a fixed message and allow explicit retry. No guessed word ranges are substituted. Disposal
revokes workers without waiting for an uncooperative text adapter; provider work keeps the existing
dictionary session's bounded teardown policy.

## Evidence and scope

`fixtures/parity/vocabulary-notebook-session-v1.json` contains 24 observations from the complete
pinned Mac notebook, explanation and dictionary sessions. The Swift driver substitutes only external
generation and a successful first dictionary hit; it records native word facts separately before
lookup, plus actual provider query/language/expression arguments. A same-file extension exposes the
original private generation Task solely so canceled late work can be released and awaited. Only
an unrelated dictionary logging call/import is omitted. No session algorithm is replaced.

The cases cover missing/merged sense selection, separate standalone/example rows, row reuse,
unchanged-input live generation, changed/removed-target cancellation, configured saved records,
activation/cancel, explicit/repeated/hide/resume dictionary operations, form/language changes,
empty form, pattern-to-expression and canonical query equality. C# executes the same operations
against the real App session and real Core children on a WPF dispatcher. Nine additional Windows
cases cover entry-scoped document identity, release/reentry inside SavedRecord, reentrant query deduplication, blocked native
word workers across navigation/disposal and same-ID effective-query edits, fixed failure/retry and a late provider across cancel/resume.
Three portable Core cases verify suspended admission and resumption by target, cancel and clear.
The shared input graph is construction data only; expected outputs remain independently Swift-owned.

The first-hit oracle does not verify source ordering, settings-based routing, misses, error fallback
or bundled-provider selection; those Mac capabilities remain unported. Native document identity is
supported by source comparison, not serialized in these 24 observations. Windows CI executes the
33 dispatcher cases; macOS compiles them and runs the portable suite and original Swift verifier.
This is the twenty-first Swift verifier. No Windows editor, physical input, model, GPU, image,
clipboard or network provider is exercised by these tests.

Notebook row presentation, explicit adoption and selection-preserving Store updates now have
[their UI adapter](VOCABULARY_EXPLANATION_UI.md). Dictionary presentation is the next integration slice. Media storage/import, review, templates, graph, export,
backup/restore, speech, source-word providers and the documented search collation gaps remain open.
No installer, artifact upload or release is produced.
