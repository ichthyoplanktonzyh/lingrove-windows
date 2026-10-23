# Vocabulary export scope and portable material projection

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

- `Cotabby/Models/Vocabulary/VocabularyExportModels.swift`: format/style/scope, configuration,
  request and portable document values (package/renderer ownership is still a later slice).
- `Cotabby/Support/Vocabulary/VocabularyExportPolicy.swift`: `selected`, `document`, `images`
  and the 64 MiB data budget contract.
- `CotabbyTests/Services/Vocabulary/VocabularyExportTests.swift`: selection identity/order,
  optional fields, variants, portable hierarchy/privacy and opt-in saved explanations.

This adapts the existing Mac rules in Core. It does not connect an export command, format bytes,
write a file, read media, create a ZIP, provide a full backup or integrate Anki.

`VocabularyExportRequest` belongs to one future export sheet and freezes its all/filtered/selected
exact entry/sense pairs. Accessing a scope returns its captured order. Constructing those lists from
notebook sorting, filters and batch selection remains presentation work. Its generated ID identifies
that one sheet; it is not a vocabulary ID. `VocabularyExportConfiguration` is a replaceable immutable
option snapshot with the Mac defaults. Expression-list behavior applies only to TXT + expressions;
media is enabled only outside that style with both content and images enabled.

`VocabularyExportPolicy.Selected` consumes one notebook snapshot and those exact IDs. It preserves
first selected entry order and within-entry sense order, deduplicates identical selections, and
rejects missing IDs or ambiguous duplicate entry IDs. It deliberately does not expand merged-from
aliases: a concurrent merge makes an old export scope stale. Empty selection is reported before
cancellation; each unique selection checks cancellation before lookup, as on Mac. The source graph
is never mutated. Selection does not combine equal expression strings into one entry.

`Document` creates a separate portable learning-material graph. Stable IDs, sense/usage creation
dates, hierarchy, text, formatting, exact UTF-16 ranges and completeness survive. Disabled optional
fields are null; enabled empty strings/lists remain present. Content absence projects to an enabled
empty list. Saved explanations opt in and retain only language/meaning/detail, excluding generation
inputs, record IDs, model and date. Source host, authorship, merge aliases, review cards, bindings,
profiles, receipts and runtime settings are not export fields. This is not the full notebook backup.

Variants preserve first raw spelling and order, excluding only empty text and the primary canonical
text/language pair. Distinct wording or optional language remains distinct; whitespace-only text is
not silently trimmed. Deduplication uses complete meaning values, not their language-only UI IDs.
Image blocks retain reference and caption even with media off; their name gains `images/` only when
media is included. These names are portable relative names, not OS file paths. `Images` deduplicates
full image-reference values from sense content only, excluding review/template images. It performs
no storage reads and does not prove that referenced files exist.

All exported collections detach on construction. The [archive snapshot](VOCABULARY_EXPORT_SNAPSHOT.md)
owns media collection; the [prepared package](VOCABULARY_EXPORT_PACKAGE.md) retains its captured bytes.
Formatters consume this reduced document. Those separate boundaries stay outside projection rules.

Thirty-seven shared probes capture observations by compiling the actual pinned Swift declarations,
including the complete original formatter solely to satisfy the unexercised `package` dependency.
The driver shares only fixed graph input construction with the relational projection verifier; it
replaces no production method. JSON in this corpus is a semantic observation transport with epoch
seconds and Mac UUID spelling, not a claim about production JSON byte formatting. Cases cover
configuration, frozen requests, exact selection/cancellation, optional fields, canonical variants,
privacy, images, styled text, fractional dates and repeated spans after a non-BMP character. Five
additional C# regressions check source preservation, immutable ownership and the complete public
export field surface independently of the observation adapter.

```sh
python3 scripts/verify-vocabulary-export-projection-macos-parity.py --mac-repo /path/to/mac-repo
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyExport
```

Later adapters cover [TXT/CSV rendering](VOCABULARY_EXPORT_NOTES.md), [JSON bytes](VOCABULARY_EXPORT_JSON.md),
[CSV/preview boundaries](VOCABULARY_EXPORT_TEXT.md), packages, media capture and [ZIP](VOCABULARY_EXPORT_ZIP.md).
Atomic destination writing, full backup/restore and the export window remain work. Native Windows clipboard,
IME, UIA, overlay/insertion and model/GPU gates are unchanged by portable export tests.
