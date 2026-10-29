# Canonical writing-card Store bridge

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, notably
`Lingrove/Services/Vocabulary/VocabularyStore.swift` and
`Lingrove/UI/Components/VocabularyFavoriteButton.swift`. This is a WPF wiring boundary over the
new canonical Store; it does not create a Store, open a file, start a model, or activate AppHost.

`IWritingVocabularyStore` is the narrow interface borrowed by the favorite control and the
sentence/dictionary card Bind methods. `CanonicalWritingViewStore(ICanonicalVocabularyStore,
SynchronizationContext)` borrows the same app-owned backend used by notebook import and browsing.
It caches only acknowledged snapshots, rejects older revisions, counts pending operations by the
capture key, and dispatches presentation notifications to the supplied UI context. The optional
`EnrichCapture` delegate can attach only the explanation service's already cached/saved record;
`Explanation`, `SaveExplanationAsync`, and `AdoptExplanationAsync` expose the same borrowed Store to
that service. Root composition supplies these callbacks and the Show-in-vocabulary navigation route.

The control shows loading/unavailable when no usable snapshot is available, disables actions during
loading and pending writes, and paints saved only from an acknowledged collected snapshot. If a
write commits but a later refresh fails, the operation remains acknowledged; the control becomes
unavailable until a successful reload or sufficiently recent notification rather than claiming it
was not saved. A retired card binding ignores late presentation callbacks, while an admitted backend
write continues. The bridge's asynchronous disposal closes its admission, drains admitted operations,
and detaches its subscription; it does not dispose the borrowed backend. AppHost must drain the
explanation service, then this bridge, then the single backend according to its ownership order.

The legacy `VocabularyStore` explicitly implements only the narrow card interface for existing
library and view tests. Production composition must not construct that archive alongside the new
canonical Store. `AnkiReviewFeature` and `AnkiReviewController` still depend on the concrete older
Store; until a separate canonical review transaction/controller port exists, the composition must
leave those advanced operations unavailable rather than create a second owner or report fake writes.
Other editor, media, review, physical desktop and visual acceptance work remains separate.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~CanonicalWritingFavoriteViewTests|FullyQualifiedName~VocabularyFavoriteViewTests'
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release
dotnet build Cotabby.sln -c Release
```

The focused STA WPF suite passed 11/11 using the real isolated canonical SQLite owner (plus the
existing library tests); it covers acknowledged save/remove, stable navigation, explanation
lookup/adoption, dismissal during an admitted write, bridge lifetime, fixed errors and an injected
post-commit read failure. The portable App suite passed 1228/1228 and the Release solution build
passed with zero warnings/errors. WPF checks include a nonactivating-window and rendered-paint check;
they do not establish physical editor focus, keyboard/mouse, IME, GPU, or visual acceptance. No
matching Mac screenshot comparison was performed.
