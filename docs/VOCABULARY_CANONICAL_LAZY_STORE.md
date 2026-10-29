# Lazy canonical Store ownership

`FrozenVocabularyStoreFactory.Create(path)` constructs one inert current-format Store. It checks
and freezes the explicit `vocabulary-v8.sqlite` path without opening a directory or database.
Subscriptions can be registered immediately. The first admitted operation opens the database
inside the existing serialized worker; concurrent first operations share that open. A failed open
is not cached and a later operation retries. Cancellation before execution does not open a file.

`DisposeAsync` closes admission and drains accepted operations before disposing an opened database.
If no operation has opened it, disposal does no notebook I/O. `OpenAsync` is an eager convenience
built from the same owner; on read/open failure it drains and disposes the candidate. The owner
never discovers or opens the previous notebook filename, and the factory does not activate AppHost.
Bundled classification installation is a separate pending integration.

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyLazyStoreTests|FullyQualifiedName~CanonicalWritingStoreTests|FullyQualifiedName~FrozenVocabularyImportStoreTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingFavoriteViewTests|FullyQualifiedName~VocabularyFavoriteViewTests'
```

The focused Inference suite passed 18/18 and the STA WPF suite passed 11/11 on Windows. Tests cover
inert construction/subscription/disposal, a real private named-file first open, retry after failed
open, cancellation before open, concurrent first reads, and disposal while initialization is blocked.
They do not establish physical desktop acceptance or production composition.
