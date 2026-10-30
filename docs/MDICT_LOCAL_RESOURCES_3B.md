# Local MDict file admission and adjacent resources

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, committed
`Lingrove/Services/LanguageLearning/MDictLibraryService.swift`,
`Lingrove/Support/LanguageLearning/DictionaryContentPolicy.swift`,
`LingroveTests/Services/LanguageLearning/DictionaryProviderTests.swift` and the synthetic
`MDictReaderTests/DictionaryFixture.swift`. Original fixtures and existing verifier pins are unchanged.

The Mac service validates MDX/MDD, keeps two cached books, adds adjacent `<stem>.css`, searches
MDD resources first and then bounded files beneath the selected folder. Windows now connects those
resources through the existing serialized service/router and restricted WebView custom-scheme route.
No folder mapping, network provider, JavaScript, model or duplicate service is added.

## Windows file boundary

The earlier source service checked path sizes/timestamps and opened ordinary FileStreams. It did
not establish the documented source-file reparse refusal: its CSS-link test exercised an unsupported
loose-file branch. The new neutral `WindowsDictionaryDirectory` owns the actual read boundary.
It checks a drive-letter's DOS device mapping against admitted local filesystem-device namespaces
before filesystem IO, opens that volume, then uses handle-relative NtCreateFile one component at
a time with FILE_OPEN_REPARSE_POINT. Each intermediate directory is checked before opening a child;
no extra object-attribute flag or multi-component reparse behavior is required. Source and resource
leaves must be ordinary disk files. Directory enumeration also uses
the acquired handle, preventing an intervening pathname substitution from redirecting a later open.
This uses ordinary user-folder ACLs rather than notebook-private ACL requirements.

Native contracts: [QueryDosDeviceW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-querydosdevicew),
[NtCreateFile](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile),
[OBJECT_ATTRIBUTES](https://learn.microsoft.com/en-us/windows/win32/api/ntdef/ns-ntdef-_object_attributes),
[FILE_FULL_DIR_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_full_dir_info)
and [GetFileInformationByHandleEx](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex).
Unicode/native buffers have explicit extents; SafeFileHandles and synchronous operation scopes own
their lifetimes. The adapter does not follow a link and inspect its target afterward.

`MDictFileLease` binds the descriptor folder to the MDX parent before opening it, admits each
explicit MDD folder separately, and borrows one MDX-folder capability for article resources.
Read leases deny ordinary writers/deletion only during an operation. Cached parser streams allow
updates between operations; native file IDs, size, modification/change stamps and folder identity
decide reuse. A filesystem that supplies no meaningful nonzero file ID is unavailable. Missing/unsafe
source admission retires the old reader rather than serving its cache.
Descriptor/resource arrays are detached before queued asynchronous work. Existing limits remain
8 GiB per source, 16 GiB combined and sixteen MDD companions. No source is modified or deleted.

UNC/device paths, redirected/SUBST mappings and reparse components fail closed. This includes
in-root links and cloud placeholders; Mac can resolve an in-root symlink, so that remains a named
Windows limitation. The admitted kernel namespace is the filename authority, not a claim about
the physical backing of externally mounted storage. File validation relies on Windows handle and
metadata semantics; it is not a full-file content snapshot of a multi-gigabyte external dictionary.

## Resource and UI behavior

MDD priority, alias depth, HTML/plain-text and cancellation budgets remain intact. If no MDD value
exists, the existing sanitized relative key may read a bounded regular file beneath the acquired
MDX root. CSS, images and fonts retain the MIME allowlist, 8,000,000-byte resource ceiling and
existing image dimension/pixel limits. A readable bounded `<stem>.css` adds the original stylesheet
link. The WebView still limits resource count/total bytes, denies navigation/scripts/downloads and
uses its independently owned private profile. Library identity, enabled-source checks and retirement
remain with the shared router. An invalid or oversized selected MDD value is not replaced with a
different loose representation. No new window or navigation design is introduced.

## Evidence

Release solution build passed with zero warnings/errors. The initial twelve service/native-root cases
passed, covering real CSS/images/fonts, MDD priority, traversal/size refusal, MDX and auto-discovered
MDD links, attachment, mismatched roots, cached-link replacement, same-length/restored-time file
replacement, temporary read leases and update/cancellation/shutdown. The cache fixture uses an
impossible header length; all-zero bytes can describe an empty book in the unchanged reader.
The related native/model/view suite passed 47/47, including an owned WebView that requests an
imported companion stylesheet through the real restricted route with scripting/web messages
disabled. Final review hardening adds component-wise traversal, nonzero identity admission and
numeric companion-priority regression. Final focused/full regression and CI outcomes are recorded
in the PR.

All files, links, pixels and profiles in tests are synthetic and privately owned. No user dictionary,
network share, model, endpoint, screen, clipboard or audio device is accessed. Real dictionary format
coverage, physical desktop/color/accessibility acceptance, in-root symlink support and exact Mac
localized-standard companion collation remain separate gates. Windows uses Shell's
[numeric filename ordering](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-strcmplogicalw)
for import priority, so part 2 precedes part 10. This is an OS-adapted filename offer, not canonical
sorting; exact native collation equality and full dictionary parity are not claimed.
