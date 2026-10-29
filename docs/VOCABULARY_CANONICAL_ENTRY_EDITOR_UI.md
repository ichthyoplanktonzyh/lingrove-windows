# Canonical notebook entry editor

This WPF editor follows the frozen macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04` sources `Lingrove/UI/Vocabulary/VocabularyEntryEditor.swift` and `VocabularyContentEditor.swift`, with archive behavior in `Lingrove/Services/Vocabulary/VocabularyArchive.swift` and coverage in `LingroveTests/Services/Vocabulary/VocabularyArchiveTests.swift` and `VocabularySenseTests.swift`. The earlier Windows source mapping and range/annotation tests are in `VOCABULARY_EDITOR_CURRENT.md`.

The canonical notebook opens one modal for the selected stable sense. It takes the complete joined entry from the acknowledged snapshot as its immutable baseline, editing a detached full value. Expression, dictionary form, selected sense core meaning, notes, tags and suggestions, ordered text blocks (style, bold, italic), image blocks/captions, and examples (text, translation, contextual meaning, completeness, add/delete) are editable. Unchanged senses, annotations, dates, IDs and evidence are retained. Expression and example text changes schedule the source range policy off the dispatcher; the current work must finish before Save. The form borrows the same canonical editor and media interfaces from the application-owned Store. Acknowledged Save closes the modal; conflict or storage errors retain the draft. Cancel discards it. An accepted save cannot be dismissed, and both ordinary closure and application retirement join image/range work and accepted writes before releasing the Store. Browse selection is refreshed after acknowledgement.

The file picker and existing-file Shell drop feed the shared bounded image preparation API. Explicit Paste image and the explanation section Paste command borrow one application-owned clipboard transfer; no clipboard data is read at construction. Reads and decoded previews borrow the canonical media boundary, and selecting a visible image opens an owned enlargement window. Cancel discards draft images; Save sends their prepared bytes through the same acknowledged media transaction. The form uses the notebook's scoped theme, Segoe UI Variable/Segoe UI, native WPF focus and scrolling, and system palette/high-contrast fallback. Windows layout and comma-delimited tag editing are platform adaptations; no Mac pixel equivalence is claimed. Saved-image enlargement is not yet offered.

The STA tests use real isolated canonical stores, including a named SQLite/media file, and a held receipt for the close/drain boundary. They cover cancel, acknowledgement, concurrent capture, conflict, stable identity/range evidence, file-image preparation/caption/persistence, and opening the selected entry from the notebook. Optional `LINGROVE_TEST_RENDER_DIR` captures editor, image and error states with light, dark and simulated high-contrast resources. These renders do not validate actual system high contrast, physical picker/focus, multiple DPI scales, screen readers, physical clipboard/drop or native desktop acceptance.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyEntryEditorWindowTests|FullyQualifiedName~FrozenVocabularyWindowControllerTests|FullyQualifiedName~FrozenVocabularyNotebookWindowTests'
```

Independent source, behavior and render review accepted this UI and passed its focused browser,
controller and editor suite 23/23. The selected-sense editor was integrated with the independently
reviewed Store transaction, including the exact UTF-8 usage-identity repair. On Windows x64
10.0.19045, .NET SDK 10.0.403, source `79f6f20` rebuilt the Release solution with zero warnings/errors
and executed the complete compiled WPF suite 671/671 using `--no-build --no-restore
--blame-hang-timeout 2m --verbosity minimal`. Core 4814/4814, portable App 1242/1242 and
noninteractive Inference 641/641 also passed in the combined integration; their relevant sources
were unchanged by the final UI/style increments. Telemetry and clipboard reads were disabled.
The broader WPF count includes the separately scoped expression application module. Final-head CI
and the physical acceptance above remain distinct checks.

## Canonical image interaction at 3b

Reference `3b53402f2cbc196b0398d133177fd80006d165b2`: `Lingrove/UI/Vocabulary/VocabularyContentEditor.swift`, `VocabularyEntryEditor.swift`, `VocabularyContentView.swift`, and `Lingrove/Services/Vocabulary/VocabularyImageTransfer.swift`. The Windows editor retains the source single-import admission, 64-block and content budgets, draft pruning and fixed failure, captured clipboard sequence, and acknowledged Save/Cancel. The application feature owns one cancellable `WindowsVocabularyClipboardTransfer` and drains the window before the transfer and Store. The canonical image view owns visible pixels, cancels stale loads, closes enlargement on retirement and joins admitted media work; refresh reconstructs views against the acknowledged Store.

Windows adapts the provider drop to existing Unicode Shell file paths only; virtual files and pixel-only drag providers are not supported. Clipboard supports the bounded encoded and DIB formats documented in `VOCABULARY_IMAGE_PASTE.md` and `VOCABULARY_CLIPBOARD_DIB.md`; compressed/profiled DIB variants remain unavailable. STA fixtures use injected clipboard offers and isolated media/SQLite, never the user's clipboard. Light, dark and simulated high-contrast renders are component evidence only; real system high contrast, physical drag/clipboard producers, keyboard focus and accessibility remain for desktop acceptance.
