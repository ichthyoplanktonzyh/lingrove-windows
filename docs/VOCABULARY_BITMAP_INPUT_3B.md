# Native Windows bitmap image input

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, committed
`Lingrove/Services/Vocabulary/VocabularyImageTransfer.swift`, `VocabularyImageStorage.swift`,
`Lingrove/UI/Vocabulary/VocabularyContentEditor.swift` and
`LingroveTests/Services/Vocabulary/VocabularyContentTests.swift`. First-provider selection,
file-before-image priority, cancellable transfer, bounded preparation, drafts and acknowledged
Save retain their existing owners. This adapts a Windows image representation.

`Win32/Vocabulary/VocabularyClipboardBitmap` copies a borrowed HBITMAP to a detached BMP envelope.
It reads a complete BITMAP descriptor, checks dimensions, computes stride/size in 64-bit arithmetic
and preserves the 24 MiB input/protocol ceiling before allocation. GetDIBits requests every row
as top-down opaque 24-bit RGB. The adapter checks count/header and zeros padding. A SafeHandle
owns its memory DC; it never selects/deletes the source bitmap or dereferences bmBits. Native
contracts: [GetObjectW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getobjectw),
[GetDIBits](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getdibits).

Clipboard CF_BITMAP follows PNG/TIFF/JPEG and DIBV5/DIB. Availability is metadata-only; copying
runs under the clipboard lease in the existing killable helper, with owner/sequence rechecks
and finally-close. HBITMAP never enters HGLOBAL APIs. OLE accepts CF_BITMAP/TYMED_GDI last,
requires a single returned medium within the offered mask and copies before finally calling
[ReleaseStgMedium](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-releasestgmedium).
Only detached bytes cross the helper pipe. The exact adapter is linked into ImageTransferHost;
no WPF/model dependency, process/job/cancellation owner or protocol budget changes.

The parent retains WIC PNG normalization, draft previews and canonical Store media/SQL identity
acknowledgement. Invalid selected data cannot fall through to another provider. Test-mode-only
helper diagnostics contain exception type/HRESULT, never messages, paths or provider content.
CF_BITMAP uses opaque RGB without source alpha/ICC claims; richer higher-priority formats retain
their existing handling. Expanded RGB must fit the byte ceiling. Unsupported GDI/OLE extraction
fails closed rather than inventing metadata or pixel semantics.

Owned native fixtures cover DIBSECTION and compatible DDB pixel rows, padding, orientation,
dimensions, invalid/oversized handles, borrowed lifetime, clipboard owner/close, medium release
and mismatched/composite tags. The fixture's release check reads a real descriptor: a null-output
GetObject size query did not prove object liveness on this host. Product admission already used
the full read. Tests never open the real clipboard or capture screen pixels.

Real cross-process tests carry a compatible DDB through the helper, canonical editor and named
Store. Another case normalizes, commits and reopens its PNG. The managed DIBSECTION provider
fixture failed OLE marshaling with 0x800706F7 on this host; in-process DIBSECTION and cross-process
DDB evidence remain distinct. A fixture DC supplies only device format for synthetic pixels.
These checks do not verify every producer, native color management, physical clipboard/drag,
accessibility or DPI. Existing UI and physical acceptance remain unchanged.

Release solution build passed with zero warnings/errors. Full Inference initially passed 757/757
before the extra composite-tag regression; the focused media/helper/editor suite passed 28/28.
Final independent Inference/WPF regression and final-head CI outcomes are recorded in the PR.
No user content, model, endpoint, audio playback, installer or release is involved.

The subsequent [owned-media repair](VOCABULARY_OWNED_MEDIA_REPAIR_3B.md) connects atomic repair of
an admitted corrupt digest leaf on supported local NTFS/TxF. Unsupported capability fails closed.
Other compressed representations and native platform gates retain their records.
