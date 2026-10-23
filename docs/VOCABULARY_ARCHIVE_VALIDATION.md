# Notebook graph admission

The stable Mac archive validates a complete document on load and before every write. This slice
ports those deterministic checks into Core. No database, codec, Save UI, backup/import, file access
or media/network operation is connected yet. A successful validation is an admitted value, not a
saved notebook or acknowledged remote receipt.

The fixed reference remains `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Mac source | Windows boundary |
| --- | --- |
| `Services/Vocabulary/VocabularyArchive.swift`, validate and validateReview | `Core/Vocabulary/VocabularyArchiveValidation.cs` checks the complete ownership graph and retained review metadata. A future serialized archive calls it without involving WPF or live focus. |
| `VocabularyArchive+ReviewCards.swift`, validateCards/validateTemplates; `VocabularyArchive+Media.swift`, validateBlockIdentities | The same validator retains the original identity scopes, counts and optional-layout rules. No card mutation or media staging is implied. |
| `Support/Review/ReviewLayoutPolicy.swift`, validate only | `Core/Review/ReviewLayoutPolicy.cs` checks layout structure and per-part/aggregate custom content. Resolution, presets, portable copies and rendering remain separate work. |
| `Support/Review/AnkiReviewPolicy.swift`, endpoint eligibility only | `Core/Review/AnkiArchiveEndpointPolicy.cs` checks retained loopback HTTP eligibility without DNS, HTTP, destination selection or rewriting stored endpoint strings. |
| `Support/Speech/SpeechExportPolicy.swift`, validFilename only | `VocabularyArchiveValidation.IsValidAudioFilename` checks the same opaque format. It verifies neither file existence, digest correctness nor audio bytes. |

Entry IDs, sense IDs, usage IDs, retired sense aliases, sense-owned block IDs and flexible-card plan
IDs share one document-wide set. Entry keys are unique; usage keys are unique within each entry;
each entry has at least one sense and at most one standalone saved sense. Template IDs are unique
only within templates. Layout part IDs and custom block IDs are unique only within that layout.
Binding IDs are unique only within that review. Legacy and preserved plan IDs are not registered in
the global set. The same legacy plan may also appear as a flexible card, matching the Mac model.

Usage text must be nonblank under Foundation whitespace trimming, fit 262,144 UTF-8 bytes and retain
valid whole composed-character ranges. Empty range arrays and overlapping/ordered ranges remain
accepted here. Sense explanations allow 128 records, usages 24; every input/result must be valid
and match the current semantic projection. Record UUID/language deduplication is not added here.
An archive commit still needs to prune stale explanations before validating an edited draft.

Layouts allow 64 ordered parts, and validate both each part's content and the combined custom
content. A non-custom part cannot own custom blocks. Empty layouts/custom parts and dangling source
references remain structurally valid; card resolution reports missing sources when used. Flexible
cards require layouts, while legacy and preserved plans may omit them. Templates allow 64 values,
nonblank names up to 1,024 UTF-8 bytes, and duplicate names with different IDs.

Review plans retain the Mac name/answer/field override budgets. Bindings preserve the original remote
origin sense ID, even after a merge; they require a positive optional note ID, nonempty profile/model,
valid loopback HTTP endpoint, identity mapping and the exact lowercase origin UUID field. Endpoint
eligibility does not authorize any request. It parses original authority spelling before .NET Uri
could broaden shorthand IPv4. Escaped hosts, empty trailing ports and leading zeros follow the
actual Foundation observations; scheme case, queries/fragments/credentials and nonempty paths keep
their source behavior.

Audio receipt fingerprints and text hashes are 64 Swift Characters, not necessarily hexadecimal or
64 UTF-16 code units. .NET text-element counting adapts this, subject to the documented Unicode
runtime-version boundary. The original filename regex permits one final Unicode newline sequence;
124-case observations include CRLF, CR, LF, NEL, LS, PS, VT and FF, but reject two final LF sequences.
An explicit regex adaptation preserves that admission behavior and leaves the retained string intact.
Actual Windows filename/byte IO remains a separate platform boundary.

Guard failures in the archive produce `InvalidArchive`; nested content/layout failures propagate
`InvalidContent` without remapping. Additional .NET-only checks reject null required values,
malformed UTF-16 and undefined enum integers that Swift values cannot represent. No new product
budgets are imposed on otherwise unbounded source fields such as notes, tags or dictionary forms.
Source validation does not check every foreign reference (for example plan example IDs, layout
source block IDs or original binding sense membership); those use-time contracts remain separate.

`fixtures/parity/vocabulary-validation-v1.json` has 124 actual pinned Mac observations. The eleventh
Swift verifier compiles exact original archive validation method bodies in a value wrapper plus
original layout, endpoint, filename and content rules. It does not compile the archive actor, create
SQLite files or call any included acquisition/search/model functions. The wrapper only supplies
the original pure instance methods with their collaborators; it supplies no replacement policy.
C# probes construct corresponding graphs and invoke the production boundaries. Four additional
cases check unchanged raw/optional values, malformed .NET values, exception categories and local
layout-reference identity scope. Existing expected corpora remain unchanged.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-validation-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

[Concurrent edit merging and stale-record pruning](VOCABULARY_ARCHIVE_DRAFTS.md) now exist as pure draft rules. [Relational projection](VOCABULARY_RELATIONAL_PROJECTION.md) is also ported. Remaining dependencies are
codec/date handling, SQLite revision comparison and durable acknowledgement, then the app-owned store
and Save/notebook UI. Validation alone establishes none of those persistence guarantees.
