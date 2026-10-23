# Windows bitmap clipboard adaptation

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`UI/Vocabulary/VocabularyContentEditor.swift` and
`Services/Vocabulary/VocabularyImageTransfer.swift`. The existing Mac paste/import/draft rules
remain authoritative. This adds Windows source-format compatibility to the same flow.

After the existing PNG → TIFF → JPEG groups, the transfer reader now tries standard `CF_DIBV5`
then `CF_DIB`. Microsoft documents [system synthesis between bitmap/DIB formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats),
including a DIB representation for `CF_BITMAP`. We never mistake an HBITMAP for an HGLOBAL or
request an arbitrary OLE object. Actual producer and OS-synthesis interoperability remains untested.

`VocabularyClipboardDib` is a deterministic Windows-format boundary compiled into both Win32 and
the short-lived helper. The reader first copies bounded clipboard memory and closes the clipboard;
only then does the wrapper validate and form a BMP file envelope for existing WIC preparation.
Null handles may try the next format. Any selected non-null malformed DIB fails without fallback.
The wrapper does no IO, native calls, model work or pixel decoding. It returns owned encoded bytes;
the archive still owns normalization, hashing, budgets and prepared-image validation.

Admission covers 40/108/124-byte info/V4/V5 headers, positive width and either row direction,
one plane, uncompressed 1/4/8/16/24/32-bit RGB and 16/32-bit bitfields. It bounds dimensions to
the existing 30,000 per axis/100 million source pixels, palette entries, external V3 masks,
contiguous/nonoverlapping masks, DWORD row stride, declared size and complete rows. `CF_DIBV5`
must declare the V5 header. The BMP header and any V4-to-V5 extension count toward the same
24 MiB transfer ceiling. Unused HGLOBAL tail bytes are discarded and image size is rewritten to
the admitted rows. No source buffer is mutated or native handle retained.

Layout follows Microsoft's [info-header fields/stride](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader),
[V4 fields](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapv4header),
[V5 profile layout](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapv5header)
and [BMP file envelope](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapfileheader).
Only inline calibrated/sRGB/Windows RGB color spaces are admitted. Linked or embedded profiles
and any V5 profile offset/size fail before decoding, so a profile cannot ask WIC to follow a local
or network filename. Compressed, OS/2/unknown header layouts remain unsupported.

The [WIC BMP codec](https://learn.microsoft.com/en-us/windows/win32/wic/bmp-format-overview) documents
32-bit V5 alpha support. Nonzero alpha requires 32 bits; BI_RGB admits only its canonical spare
byte, while bitfield alpha must be contiguous and disjoint from RGB. A V4 header carrying alpha
is extended to V5 with perceptual intent and zero profile fields before WIC sees it, preserving
the existing color masks and rows. Sixteen-bit alpha is explicitly unsupported.

Tests use literal DIB rows and app-owned memory, never the runner clipboard. Structure cases cover
headers, row direction, palette/mask offsets, malformed dimensions/sizes/planes/depth/compression,
profile rejection, alpha admission, input/output budgets, unused tail trimming and V4 extension.
Reader cases cover metadata-only admission, encoded-format priority, null bitmap fallback,
detachment/close ordering and no fallback after invalid selected data. Ten actual WIC/archive/PNG
cases check red top/blue bottom pixels for all three headers and both directions, plus V4/V5
BI_RGB/bitfield alpha. These are format/pixel regressions, not evidence of physical keyboard,
Paint/browser/Office clipboard production, color-profile parity or complete Mac image-provider parity.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Windows CI; no clipboard mutation:
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyClipboard
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyDibPreparationTests
```
