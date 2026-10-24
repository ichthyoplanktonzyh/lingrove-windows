# Current stable editor annotation behavior

This slice upgrades **example-text/expression editing and the shared Foundation text-range adapter**
from Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` to
`cc468b89f5c360cc7210fac4f1a8b52197870bf2`. Other modules keep their documented reference revisions.
The port adapts the existing stable product; it does not redesign vocabulary ownership or editing.
[The current-stable delta](MAC_STABLE_DELTA.md) records work beyond this slice.

## Ownership and flow

| Mac responsibility at the current reference | Windows boundary |
| --- | --- |
| `Support/Vocabulary/VocabularyPolicy.swift:reconciledRanges` | `Core/Vocabulary/VocabularyPolicy.ReconciledRanges` |
| `UI/Vocabulary/VocabularyEntryEditor.swift` original-text/expression `onChange` | `VocabularyEntryEditingPolicy`, borrowed by the existing modal window |
| NSString case/diacritic search and composed-range calls | `Core/Text/FoundationSubstringMatcher`, `FoundationComposedText`, immutable `FoundationEditorTextData` |
| `VocabularyPolicy.valid` shared by capture/archive and rendering | Existing `FoundationTextRanges` facade now returns one caller-owned composed-text map |

An original-text edit assigns the new text and replaces only its ranges. Translation, translation
language/variants, contextual meaning/language/authorship, completeness, IDs, times, source application
and saved explanation records remain in the draft. Archive commit still prunes generated records
whose semantic input became stale. Editing a contextual meaning explicitly still acquires user
authorship through the existing archive merge.

Expression edits recompute examples belonging only to the editor's selected sense. Other senses
retain their ranges and annotations. Dictionary-form editing has **no immediate re-anchoring
callback** in the source: its current value is read by the next original-text or expression edit.
Canonical String equality retains the source's `onChange` scheduling behavior.

The WPF dispatcher assigns the new draft text/expression immediately and starts owned background
range work. Each affected usage retains the expression/form/text captured by its particular source
event; a dictionary-only edit does not retroactively change another pending usage's inputs. New
work cancels old work and carries still-pending usages forward. Publication checks the operation
revision and current canonical text, changes only ranges, and preserves annotation edits made while
the worker ran. Removing usages prunes their work. Save waits for current range publication; Cancel
remains available, and modal retirement cancels/joins every admitted worker before lifetime disposal.
This Windows scheduling adapter prevents long Unicode prefixes from blocking the UI; the raw matcher
still has a worst-case quadratic comparison path, so its CPU work is explicitly cancellable rather
than described as a universally linear search implementation.

The policy normalizes each candidate with NFC and Character whitespace splitting, tries expression
first, then dictionary form only if no valid expression occurrence exists. Matching has no word
boundary, stemming or pattern interpretation. Each raw UTF-16 hit expands to composed ranges and
passes shared admission. Search resumes at the **raw** hit's end, retaining duplicate expanded spans.
The matching text itself is never normalized or rewritten.

The matcher owns one text/needle attempt and short-lived fold cursors. A complete expansion must
drain before its original offset advances; `ß → ss` cannot expose one synthetic `s`. A literal unit
is tested before folding **at the same position**, preserving source order. Recorded compatibility
expansions apply only to the searched text; they are not a symmetric equivalence relation.

A composed-text map owns the original string and cached base segments for one caller operation.
NSString composed expansion can be asymmetric (`a` + ZWJ + `b`), so two independent endpoint flags
cannot express its validity rule. Capture, archive, saved highlighting and explanation/review range
admission now borrow the same range-expansion operation. UI-only literal selection retains its
separate extended-grapheme map. Context-window expansion keeps the original prompt rules and budgets;
no new prompt content or transmission is introduced.

## Data and evidence

`fixtures/parity/vocabulary-editor-current-v2.json` contains **182 native observations**:
155 re-anchoring cases, 14 direct range-admission cases, 8 original-text callbacks and 5 expression
callbacks. One adapts the new original regression
`VocabularyPolicyTests.reconciledRangesReanchorExpressionAfterExampleTextEdit`; 181 are explicit
source-policy extensions. These are not 182 original Swift test methods.

The verifier compiles the complete current policy and its actual value/policy dependencies. It
extracts the two actual editor callback bodies into a value wrapper and adds the explicit SwiftUI
equality scheduling guard; it does not reproduce their mutation rules in Python or replace the
search routine. JSON example payloads preserve all fields, including retained generated records.
The second sense intentionally has a different valid range to detect accidental whole-entry edits.
All expectations were captured from that native executable before C# implementation, never fitted
to the port. Both runners lock reference revisions, locale, unique IDs/hash, operation counts and
source/test provenance. Capture uses an explicit new destination; ordinary verification never writes.

Coverage includes expression precedence/form fallback, canonical spellings, repeated/nonoverlapping
hits, accents and case expansions, Greek/Turkish/Hangul/Indic text, emoji modifiers/ZWJ/flags,
Prepend, CR/LF, literal control/ignorable characters, compatibility direction and incomplete
expansion rejection. Direct validity cases ensure a range accepted by editing is also accepted by
capture and archive. Three extra portable facts cover full-archive admission of returned ranges,
malformed .NET UTF-16 and frozen data identity.

The original v43 `vocabulary-editor-v1.json` and its 29-observation native verifier remain unchanged
as historical evidence. Current C# tests continue using its 24 unchanged tags/save/move rules; its
five superseded annotation-reset outcomes are excluded explicitly. A legacy reset implementation
is not retained in production to satisfy old expectations.

The embedded scalar data was generated by observing **all 1,112,064 valid Unicode scalars** on
macOS 26.6.2 (25G83), Core Foundation 5026.6, harness locale `en_US`. It records 1,944 nonidentity
folds and 280 source-only compatibility transforms, compressed properties and combining classes.
The offline tool alone uses private Darwin `CFUniChar` symbols. Windows uses managed code and an
embedded immutable resource, with no Foundation binary, network access or runtime data generation.
[Data provenance, hashes and licenses](../src/Cotabby.Core/Resources/Foundation/NOTICE.md) accompany
the resource. Unicode 17.0.0 InCB rows are pinned separately for the raw search's conjunct boundary.

This establishes the measured editor corpus on the recorded Darwin boundary. It does **not** prove
all Foundation operations, all Unicode strings, arbitrary locale/OS versions, or notebook
`localizedStandardContains` equivalence. The existing notebook search adapter remains a separate
platform boundary. Further Unicode/OS cases require fresh native observations, not fixture-specific
branches. Windows CI runs the same portable cases, plus compiled WPF component tests.

The modal tests now retain annotations while checking re-anchoring, acknowledged save/stale-record
pruning, expression/selected-sense scope, dictionary-form timing, owned long-prefix cancellation/dispatcher progress, and an actual edit→SQLite→notebook
highlight round trip for a composed-range boundary. They do not establish physical keyboard/IME,
focus activation, pixel placement, or a complete current Mac editor UI. Current tag chips/suggestions,
localization, window geometry and other visual deltas remain migration work. No installer, artifact
upload, model download or release is produced.

```sh
python3 scripts/verify-vocabulary-editor-current-macos-parity.py --mac-repo /path/to/macOS-lingrove
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --no-restore
```

Offline data recapture is a separate, explicit maintenance operation. It requires macOS and writes
only a nonexistent destination:

```sh
python3 scripts/capture-editor-foundation-data.py --output /tmp/new-editor-text-data.json
```

Review a new capture's OS/framework/generator identity and run the full source corpus before
replacing any production data. CI does not silently recapture or update the frozen table.
