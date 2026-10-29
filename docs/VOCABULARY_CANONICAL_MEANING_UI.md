# Canonical meaning and mapping UI

The current notebook upgrades this historical 35b checkpoint at [3b meaning and classification](VOCABULARY_MEANING_CLASSIFICATION_3B.md).

Frozen macOS reference: `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, `Lingrove/UI/Vocabulary/VocabularyMeaningEditor.swift`, `VocabularyMeaningMappingEditor.swift`, `VocabularyMeaningDetail.swift`, `VocabularySenseSemanticSection.swift`, `VocabularySemanticPresentation.swift`, `VocabularyNotebookView.swift`, and `Lingrove/Support/Vocabulary/VocabularySemanticEditingPolicy.swift` / `VocabularySemanticPolicy.swift`. Tests include `LingroveTests/UI/Vocabulary/VocabularyMeaningViewTests.swift`, `VocabularyNotebookViewTests.swift`, `LingroveTests/Services/Vocabulary/VocabularySemanticTests.swift` and `LingroveTests/Support/Vocabulary/VocabularySemanticEditingTests.swift`.

The canonical notebook uses its one borrowed `ICanonicalVocabularySemanticStore`. The meaning sheet pins the displayed label's ID and actual language at opening, edits only its title and definition, and leaves other labels/roles/provenance intact. Creation chooses concept or function and can link the initiating sense. The mapping sheet fixes or offers the source sense and meaning as in the entry point, displays the example and restricted wording, and edits conditions/nuance while retaining pattern and evidence identity. Moving a mapping clears only old node role bindings. Proposed mappings expose an explicit confirmation action; change/removal do not discard lexical evidence. Meaning deletion asks for confirmation and removes dependent semantic associations and classification memberships through the same acknowledged Store; expressions and category definitions remain.

Mapping summaries show the first collected example as a representative only when there are no
cited evidence IDs and no pattern restriction. Explicit evidence must match a visible example;
neither missing evidence nor an uncited pattern falls back to an unrelated example. Presentation
does not add or rewrite evidence IDs.

Forms retain detached baselines, cancel without writing, disable dismissal while an accepted save drains and retain a failed draft with its error. The notebook joins direct semantic writes and modal saves before unsubscribing. Later navigation supersedes a form's optional reveal, and Store revision notifications remain the browser's existing bounded responsibility. Pure `VocabularySemanticFormPolicy` ports the frozen rename/classify/remove-node editing rules using frozen `SourceDate`; Store validation and three-way merge remain separate.

Independent review accepted the representative-example repair after Core 4/4 and meaning-view
11/11 checks. The combined Windows integration built the Release solution with zero warnings or
errors, and the documented notebook/meaning WPF filter passed 35/35. Light/dark form renders were
also inspected for hierarchy and legibility. These are automated/component and offscreen checks;
physical desktop acceptance remains open.

This meaning-node and mapping slice is complemented by the canonical category sheets, category manager and [usage-pattern editor](VOCABULARY_CANONICAL_PATTERN_UI.md). The WPF adaptation uses the notebook's scoped theme, Segoe UI Variable/Segoe UI and 96-DPI units. The STA tests drive real isolated in-memory and named SQLite Stores, reopen a saved node, and cover linked creation, locale/label identity, mapping evidence/pattern preservation, proposal confirmation/removal, cancellation, conflict, delete, and accepted-write drain. Optional offscreen light/dark/simulated-high-contrast captures do not establish physical desktop, real high contrast, per-monitor DPI, screen-reader or keyboard acceptance.

```powershell
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularySemanticFormTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~FrozenVocabularyMeaningUiTests|FullyQualifiedName~FrozenVocabularySenseActionsTests|FullyQualifiedName~FrozenVocabularyNotebookWindowTests|FullyQualifiedName~FrozenVocabularyWindowControllerTests'
```
