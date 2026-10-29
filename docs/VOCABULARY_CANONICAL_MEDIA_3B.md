# Canonical media input at the 3b reference

The source reference is macOS `3b53402f2cbc196b0398d133177fd80006d165b2`:
`Lingrove/UI/Vocabulary/VocabularyContentEditor.swift`,
`Lingrove/Services/Vocabulary/VocabularyImageTransfer.swift`,
`VocabularyImageStorage.swift`, `VocabularyArchive+Media.swift`, and
`LingroveTests/Services/Vocabulary/VocabularyContentTests.swift`. The source prefers the first
provider's file URL over its image data, cancels a stalled provider, normalizes the first frame
to PNG, and retains a repaired pre-existing file if a later metadata commit fails.

The existing Windows image paste owner now also accepts a bounded subset of standard BI_RLE4 and
BI_RLE8 packed DIB data. It validates each encoded, absolute, end-of-line, end-of-bitmap and delta
command against the dimensions, palette and declared byte extent before wrapping it in a BMP.
Only bottom-up, exactly terminated streams are admitted. The same isolated clipboard helper,
WIC normalization, draft preview, canonical Store media transaction and acknowledged Save own
the rest of the flow. No clipboard is read by the tests. The existing input and image limits,
untrusted provider isolation and transaction rules remain in force.

Linked and embedded bitmap profiles, other compressed bitmap layouts, virtual-file and pixel-only
OLE drops remain unsupported. Ordinary WIC formats with a readable color context use the existing
color conversion. Current-format restore also still rejects a corrupt existing digest leaf: the
current Windows rename operation cannot bind its replacement target to the verified file identity
against a same-user replacement race. It does not delete that file or claim a successful restore.
These are explicit platform gaps, not evidence of complete 3b media parity.

Synthetic native Windows tests cover RLE4/RLE8 validation and real WIC pixel normalization, plus
RLE8 through the canonical editor and an isolated named Store. Physical clipboard producers,
drag providers, Shell interaction and desktop acceptance remain unverified.
