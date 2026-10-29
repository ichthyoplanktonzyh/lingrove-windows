# Selected-text reading: isolated checkpoint

Reference: macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, including
`App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift`,
`Models/LanguageLearning/LearningReadingSelection.swift`,
`Services/LanguageLearning/LearningReadingSelectionReader.swift`,
`Support/LanguageLearning/LearningReadingPolicy.swift`, `LearningReadingLookupPolicy.swift`,
`LearningReadingLanguageResolver.swift`, `LearningReadingContextPolicy.swift`,
`LearningTranslationPrompt.swift` and their corresponding coordinator/policy tests.

The pure library preserves bounded complete selected text, source/context/hint language evidence,
unknown language, and the distinction between whole-sentence translation and contextual or
standalone lookup. Language hypotheses, script compatibility and word segmentation are explicit
platform inputs. Source-derived C# examples are not direct Swift executions. Context must retain
an admitted exact occurrence; a selected string is never found again by searching the document.
Reading can carry unknown source through the existing translation options, budget and prompt family:
generic omits a source name, Hy-MT retains its target-only prompt and TranslateGemma asks the model
to infer the language. Existing known-source prompt bytes and writing defaults remain unchanged.

The isolated dispatcher session borrows a read-only capture lease, translation provider and rich
lookup owner. It verifies the receipt before presentation and after model work, tracks cancellation
and shutdown, and carries separate translation and lookup destinations. Endpoint destinations
require explicit authorization and display the actual sanitized HTTP(S) URL before provider work;
no transport or credential owner is constructed here. A small WPF reading card shows source,
destination, loading, result and error hierarchy; its nonactivating placement uses the existing
learning-panel adapter. A 96-DPI test rendering was inspected; physical DPI, accessibility and
host focus behavior remain unverified.

This is not an activated product feature. The native readonly selection adapter, platform language
and script detector, source-faithful word segmentation, selection icon/shortcut, real translation
and dictionary-card integration, context extraction, detail actions and AppHost/settings composition
remain work. In particular, existing dictionary state assumes a known language; unknown-language
lookup must not be silently labeled English. Pointer, screenshot/OCR and clipboard sources are not
implemented here. No model, external endpoint, user editor or clipboard is exercised by tests.
