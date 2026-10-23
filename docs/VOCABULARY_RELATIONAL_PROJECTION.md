# Typed relational notebook projection

The stable Mac SQLite owner stores entries, senses, usages, sense/usage explanations, authored
blocks, reviews, bindings and templates separately. This slice ports its typed row values and pure
bidirectional projection into Core. It neither opens a database nor defines a JSON/date codec,
performs SQL, acknowledges a write or connects Save UI. Portable/storage compatibility remains an
explicit later boundary; typed equality does not establish encoded-byte compatibility.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source | Windows boundary |
| --- | --- |
| `Models/Vocabulary/VocabularyDatabaseModels.swift`, table/key/record/typed payload values | `Core/Vocabulary/VocabularyDatabaseModels.cs` owns closed table descriptors, typed header/leaf alternatives, parent/order columns and frozen extra indexed values. Payload encode/decode is not ported in this slice. |
| `Support/Vocabulary/VocabularyDatabaseProjection.swift`, rows/document/Graph | `Core/Vocabulary/VocabularyDatabaseProjection.cs` strips child relationships into rows, reconstructs ordered owners and reprojects the complete result to check consistency. A future SQLite owner supplies decoded typed values. |

Entry headers omit senses. Sense headers omit examples, explanations and legacy review, preserving
absent versus empty authored content/cards with optional empty markers. Usage headers omit records;
review headers omit bindings. Metadata stays with its stable identity. Parent/position columns own
relationships, so a note edit changes one sense row while moving an unmodified usage changes its
parent without renaming the usage or its explanation rows. Actual SQL write ordering and cascade
safety remain part of the unimplemented SQLite owner.

IDs use the original uppercase UUID spelling for relational keys. Explanations and bindings include
the owning row ID in their keys; a cached record/binding UUID can occur under different owners
without overwriting another relationship. Reviews retain distinct `:legacy` and `:card:<plan UUID>`
keys, with legacy before flexible cards. Table/key identity and extra string values follow Swift
canonical equality without changing retained spelling. Row values and exposed collections are
immutable snapshots; future database change detection can compare them before encoding.

Reconstruction groups rows by table/parent and requires contiguous positions starting at zero. It
requires table-appropriate payload alternatives, then projects the reconstructed graph back and
compares every typed row/key/indexed value. Orphan rows, duplicate keys, hidden children in headers,
wrong IDs/parents/indexed columns, hidden optional content/cards, changed review ordering and a false
template-presence flag fail with `InvalidArchive`. .NET-only null lists/nodes, undefined table enums
and null required projection collaborators also fail recoverably rather than dereferencing them.

This check establishes a lossless row representation, not semantic notebook validity. Removing a
whole usage and all its record rows describes a valid changed representation. Removing binding rows
may likewise reconstruct but leave an active binding ID invalid; the separate archive validator
must reject that document before publishing. Metadata version, foreign-source resolution, content
budgets, bindings and admission rules still belong to their existing boundaries. The future archive
must use both projection checks and complete admission on load/commit.

The thirteenth Swift verifier compiles complete original typed database models and projection along
with the pinned value/key dependencies. Its 26 actual observations compare table metadata, row keys,
parents/order/indexed values, full typed roundtrip equality, optional markers and malformed row
failures. Neither driver calls payload encode/decode or SQL. Cases include independent legacy/card
binding relationships and owner-scoped reuse of explanation UUIDs. Five additional C# cases cover
localized row changes, usage reparenting, frozen/canonical values, null graphs and the distinction
between reconstruction and semantic admission. Existing fixture expected values are unchanged.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-projection-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

Codec/date/schema handling, SQLite file identity/locking/durable revision comparison, media lifetime,
archive/store publication and Save/notebook UI remain migration work.
