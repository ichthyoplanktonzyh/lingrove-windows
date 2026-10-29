# Canonical writing Store boundary

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, especially
`Lingrove/Services/Vocabulary/VocabularyArchive.swift`, `VocabularyStore.swift`,
`Lingrove/Support/Vocabulary/VocabularyPolicy.swift`, `VocabularySensePolicy.swift`,
`VocabularyNotebookProjection.swift`, `VocabularySemanticPolicy.swift` and
`Lingrove/Support/LanguageLearning/LearningExplanationPolicy.swift`. This is an isolated
current-format implementation and composition boundary. The subsequent
[application composition](VOCABULARY_CANONICAL_APP.md) shares this owner across writing and notebook UI.

`FrozenVocabularyStoreFactory.OpenAsync` admits only the explicit fresh file name
`vocabulary-v8.sqlite` in a caller-selected directory, with the existing private file/identity and
lock adapters. It never discovers or opens `vocabulary.sqlite`. The caller must create exactly one
`ICanonicalVocabularyStore` and share that instance with notebook import/browsing and writing-card
adapters; the historical Store must not be constructed as a parallel writer. The factory opens the
new file on a worker. A failed or cancelled open does not publish an owner; errors never reset an
existing destination. File selection and AppHost composition are separate integration work.

`ICanonicalVocabularyStore` extends the existing import Store with writing Save, exact Remove,
saved-explanation lookup, generated-record attachment and explicit adoption. Input types are the
existing detached live writing-card values; the conversion adapts current capture values and does
not read a prior notebook. New captured sentences receive independent source identity, retain the
usage UUID, exact text/ranges, translation and supplied application provenance, and use the frozen
`captured` source kind. UI callers use `CanonicalWritingPolicy.SavedDestination` over their latest
acknowledged collected snapshot; mutation receipts carry the committed revision and actual stable
entry/sense/usage destination. Saving always commits as in the source. A missing exact Remove,
unmatched explanation or refused adoption returns `Changed=false` without advancing revision.

Removal changes personal collection membership and leaves catalog, occurrences, interpretations
and source evidence intact. Empty collections are removed only when no other personal state remains.
Source-compatible unknown-completeness matching can resolve one exact text/range occurrence; an
ambiguous match fails rather than guessing. A repeated usage retains identity and merges translation
variants. Stored explanations match exact input and language, fill only unowned wording, and never
overwrite a user-authored meaning. Explicit adoption resolves the current collected usage or sense,
checks the current input and marks the meaning user-authored; removed or changed destinations do not
reappear. No model or network request is started by a write.

Before committing, the shared owner validates canonical lifecycle relationships, reconciles semantic
mapping evidence against changed source/occurrence/meaning, marks affected mappings proposed, and
prunes explanations whose exact input became stale. The SQLite owner then performs its validation,
revision check and atomic write, and only an acknowledged snapshot is enqueued for observers.
Callbacks are delivered on the existing coalescing worker; UI consumers must dispatch and use revision
ordering. Disposal closes admission immediately, drains admitted operations and notifications, then
releases the database; an in-flight observer must not synchronously wait for its own Store disposal.

The live explanation service uses Windows `DateTimeOffset` values. A lookup converts a canonical
Foundation Double to a representable Windows timestamp for the returned detached service value; the
canonical record itself remains unchanged. An unrepresentable timestamp or unsupported language/
context is refused rather than silently inventing a value. Existing endpoint selection, input
freshness, IME and privacy gates remain owned by the writing coordinator.

The [lazy Store](VOCABULARY_CANONICAL_LAZY_STORE.md) subsequently enabled the exact frozen bundled
classification catalog within its serialized read/commit span. Media files, editor/review mutations,
backup/restore, physical desktop acceptance and complete visual checks remain incomplete.
These tests do not establish those features or cross-platform storage
compatibility. No pre-existing user file is opened, modified or migrated by this slice.

Executed on the Windows host:

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingStoreTests|FullyQualifiedName~FrozenVocabularyImportStoreTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName!~ClipboardTextReaderTests'
dotnet build Cotabby.sln -c Release
```

The focused suite passed 13/13, the noninteractive Inference suite passed 617/617, and the Release
solution build passed with no warnings or errors. Tests use isolated files/in-memory SQLite, including
an old-name sentinel, reopen, rollback and drain. They do not execute a physical card or native model.
