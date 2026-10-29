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

Packed V5 DIBs also admit bounded embedded ICC profiles after the pixel data. The transfer helper
copies only the admitted bytes; the image preparer initializes a WIC color context from memory,
converts to sRGB, and produces a metadata-free PNG. A linked V5 bitmap profile is rejected before
WIC receives the data, including for a chosen BMP file, so it cannot name a network path. Other
compressed bitmap layouts and OLE formats other than the explicitly listed ones remain unsupported. Ordinary WIC
formats with a readable color context use the existing color conversion. Current-format restore
also still rejects a corrupt existing digest leaf: the
current Windows rename operation cannot bind its replacement target to the verified file identity
against a same-user replacement race. It does not delete that file or claim a successful restore.
These are explicit platform gaps, not evidence of complete 3b media parity.

An in-process synthetic COM probe holds an `IMarshal::GetUnmarshalClass` callback and observes
that `CoMarshalInterface` cannot return until it is released. Thus passing a dropped object from
the UI apartment to a child process is not by itself a cancellable provider bridge. The editor
instead has a dedicated native drop surface whose child HWND and OLE target belong to a helper.
It accepts the first existing-file path, virtual file descriptor/content, encoded PNG/TIFF/JPEG,
or bounded DIB; the helper returns only detached image bytes. It never passes a source pathname
or foreign COM interface into the editor. File leaves are opened without following reparse points.
The helper joins a kill-on-close job before receiving data, with 512 MiB process/job memory limits
and a one-process limit;
startup, admitted transfers, and the receiver's total lifetime are bounded. Editor close cancels
and joins it before destroying the host HWND. A missing helper or failed image is shown explicitly.
The job isolates lifetime and resource accounting; it is not a reduced-privilege sandbox.
The existing file-drop target still covers the explanation area. The dedicated surface is a
Windows airspace adaptation for virtual and pixel data, not a claim that every OLE representation
or a CF_BITMAP-only provider is supported.

Synthetic native Windows tests cover RLE4/RLE8 validation and real WIC pixel normalization,
embedded-profile rejection/bounds and a synthetic linear-light profile converted to sRGB, plus
RLE8 and a profiled bitmap through the canonical editor and an isolated named Store. Synthetic
cross-process COM tests exercise image and virtual-file HGLOBAL/IStream payloads through the helper,
editor and named Store, plus a blocked provider killed and joined before editor dismissal. The test
also holds a ready helper across window retirement and injects presentation failures: every returned
helper is joined before retry or host destruction, including when no window state was published.
An independent review run of the earlier readiness test reported 13/14 with `IsWindow` still true;
the isolated case then passed. That run did not record the window's owner PID, so it cannot
distinguish a reused HWND from delayed destruction. The revised tests pin the original process
instance, verify its exit and drained result, and wait for that PID to stop owning the HWND; a
reused HWND owned by another process is not treated as the original helper.
The helper's explicit marshal-token mode exists only to supply synthetic test providers; normal product
startup does not request it. Physical clipboard producers, drag providers, Shell interaction,
scaling and desktop acceptance remain unverified.
