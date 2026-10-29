# Frozen entry import planning

This isolated pure policy adapts `Lingrove/Support/Vocabulary/VocabularyEntryInputPolicy.swift`,
`VocabularyImportedExamplePolicy.swift`, and `Lingrove/Models/Vocabulary/VocabularyEntryInput.swift`
at Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. It shares the existing bounded input
validation and word-list mapping. Planning uses a detached joined projection, indexes each
headword's sense and example keys, retains mutable batch lists until final projection, and returns
actual counts and entry/sense selection IDs. It preserves existing raw source evidence and restores
collection membership separately from lexical identity. The plan has no persistence or Store owner;
a future archive must re-plan under its write lock and acknowledge before publishing.

Focused tests cover new/duplicate entries, stable selection, example translations and conflict,
independent unannotated contexts, batched matching, and re-collection of uncollected evidence.
They are Windows-authored cases, not native Swift execution. Additional frozen-source regression
and native interchange checks remain required before production activation.
