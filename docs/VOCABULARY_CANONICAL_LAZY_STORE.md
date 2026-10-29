# Lazy canonical Store ownership

`FrozenVocabularyStoreFactory.Create(path, installBundledClassification: true)` constructs one inert current-format Store. It checks
and freezes the explicit `vocabulary-v8.sqlite` path without opening a directory or database.
Subscriptions can be registered immediately. The first admitted operation opens the database
inside the existing serialized worker; concurrent first operations share that open. A failed open
is not cached and a later operation retries. Cancellation before execution does not open a file.

`DisposeAsync` closes admission and drains accepted operations before disposing an opened database.
If no operation has opened it, disposal does no notebook I/O. `OpenAsync` is an eager convenience
built from the same owner; on read/open failure it drains and disposes the candidate. The owner
never discovers or opens the previous notebook filename, and the factory does not activate AppHost.
The first operation reads and conditionally installs the frozen bundled classification within one
exclusive database span. Only a successful installation commits and publishes a new revision; an
unchanged catalog does neither. A catalog is cached only after installation admission succeeds;
load or admission failures remain retryable. Passing `false` skips loading the resource. The eager `OpenAsync`
uses the same owner and defaults to installation as well. The pure installation policy and frozen
source provenance are described in `VOCABULARY_CLASSIFICATION_INSTALLER_FROZEN.md`.

The isolated Store tests cover a real named-file install and reopen, concurrent first reads, a
failed catalog load followed by retry, disabled loading, and SQL rollback followed by retry. These
checks do not activate the Store in AppHost or establish physical desktop acceptance.

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyLazyStoreTests|FullyQualifiedName~CanonicalWritingStoreTests|FullyQualifiedName~FrozenVocabularyImportStoreTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingFavoriteViewTests|FullyQualifiedName~VocabularyFavoriteViewTests'
```

The focused Inference suite passed 18/18 and the STA WPF suite passed 11/11 on Windows. Tests cover
inert construction/subscription/disposal, a real private named-file first open, retry after failed
open, cancellation before open, concurrent first reads, and disposal while initialization is blocked.
They do not establish physical desktop acceptance or production composition.
