# Canonical usage-pattern editing

Reference: macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, `Lingrove/UI/Vocabulary/VocabularyUsagePatternEditor.swift`, `VocabularySenseSemanticSection.swift`, `VocabularySemanticPresentation.swift`, `VocabularyMeaningMappingEditor.swift`, `Lingrove/Support/Vocabulary/VocabularySemanticPolicy.swift` (`removePattern`) and `LingroveTests/Services/Vocabulary/VocabularySemanticTests.swift` (`patternRemovalDoesNotBroadenARestrictedAssociation`).

The sense organize action creates a detached wording draft. The editor retains pattern identity, sense, structured slots and provenance; a pattern with slots displays its template read-only. Users can edit note, formality, medium, region and newline-separated collocations. Save uses the borrowed canonical semantic Store with the original baseline; cancellation leaves it untouched, conflicts retain the draft, and accepted saves drain before retirement. Summaries display practical restrictions and suggested status. Mapped patterns appear once with their grouping and can be edited from a confirmed grouping; standalone patterns offer edit and confirmed removal. Removal deletes references to the pattern rather than broadening their scope, and leaves entries and evidence intact. The frozen source displays suggested wording but has no separate pattern confirmation command; grouping confirmation remains a separate operation.

The WPF sheet uses the scoped notebook theme and 96-DPI units; its scrollable details adapt the source disclosure. STA tests use isolated SQLite and injected held operations to exercise create, cancel, conflict, locked slot templates, retained identity/evidence/provenance, deletion cleanup and retirement. Optional offscreen light/dark/simulated-high-contrast renders are not physical desktop, real high contrast, DPI or accessibility acceptance.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter FullyQualifiedName~FrozenVocabularyMeaningUiTests
```
