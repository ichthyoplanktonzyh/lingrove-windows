# Canonical sense and usage actions

Frozen reference: macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, `Lingrove/UI/Vocabulary/VocabularyNotebookView.swift` (`entryDetail`, `VocabularyEntryDetail`, `VocabularyExampleCard`, delete alert and `showSense`), `Lingrove/UI/Vocabulary/VocabularyNotebookRoot.swift`, `Lingrove/Services/Vocabulary/VocabularyArchive.swift` and `LingroveTests/UI/Vocabulary/VocabularyNotebookViewTests.swift` / `LingroveTests/Services/Vocabulary/VocabularySenseTests.swift`. The Store transaction contract and raw-evidence behavior are documented in `VOCABULARY_CANONICAL_SENSE_STORE.md`.

The selected sense detail now offers New sense, Merge sense, and Delete beside Edit. Merge chooses another collected sense of the same entry. Delete requires an owned confirmation; cancel has no Store effect and confirmed removal uncollects the sense, retaining its raw catalog and evidence. Each displayed usage has a Move usage menu with other collected senses and New sense as destinations. Create and merge navigate to the acknowledged destination; move keeps the source selected, matching the frozen view. Current filters and organization remain intact. Unsupported review/export and semantic/category mutations remain visibly disabled.

The window borrows `ICanonicalVocabularySenseStore` from the same application-owned Store. It submits exact stable IDs and awaits the acknowledged result. Only one action is admitted at a time; detail controls show a busy state and errors are visible and retryable. Menus retire when the detail changes, and an action receipt cannot override a later selection/filter/navigation. During shutdown the window closes a pending confirmation, drains any accepted mutation before unsubscribing, and then lets its owner dispose the Store. The component creates no Store and does not read older data.

The isolated STA tests drive the actual controls and real SQLite Store for create, merge, move to existing/new sense, cancellation and removal, checking stable selection/usage identity and retained raw evidence. Held receipts exercise late selection, busy/error state, stale menu rejection and shutdown drain; a confirmation retirement test covers the modal boundary. Offscreen light/dark/simulated-high-contrast captures include detail, target menu, confirmation and error. Native target-menu keyboard Enter is exercised by routed input; actual desktop pointer/keyboard, screen reader, Windows high contrast, multi-DPI and physical confirmation/focus behavior remain acceptance work. The Windows menu style adapts the source hierarchy to the existing scoped WPF theme; it does not claim Mac pixel matching.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySenseActionsTests|FullyQualifiedName~FrozenVocabularyWindowControllerTests|FullyQualifiedName~FrozenVocabularyNotebookWindowTests|FullyQualifiedName~FrozenVocabularyEntryEditorWindowTests'
```

Independent source/render review and a rebuilt focused WPF suite passed 30/30. Final scoped
integration source `5b5d820` on Windows x64, SDK 10.0.403, rebuilt the Release solution with zero
warnings/errors. The same compiled WPF filter passed 30/30, Core `FrozenVocabularySenseActionTests`
passed 4/4 and Inference `FrozenVocabularySenseStoreTests` passed 3/3. Tests used Release,
`--no-build --no-restore --verbosity minimal`, telemetry and clipboard reads disabled. These
isolated checks do not establish the physical acceptance above; final-head CI is separate.
