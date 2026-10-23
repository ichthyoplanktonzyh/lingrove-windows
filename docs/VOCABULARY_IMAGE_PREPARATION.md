# Image preparation library

This slice adapts Mac `VocabularyImageStorage.prepare` and byte/reference validation. It is an
unconnected library: the editor still preserves image references and captions but cannot yet import
or persist image bytes. Drafts own prepared bytes; the future archive media owner must validate and
stage them before committing metadata. Preparing never writes a file or changes a notebook.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac responsibility | Windows mapping |
| --- | --- |
| `Services/Vocabulary/VocabularyImageStorage.swift`: input/decode/dimension limits, frame zero, orientation, bounded PNG and reference | `Win32/Vocabulary/WindowsVocabularyImagePreparer.cs` and `WindowsVocabularyImageFrame.cs` |
| `Models/Vocabulary/VocabularyContentModels.swift`: detached prepared reference/data | Core `VocabularyPreparedImage`, with defensive byte copies |
| `Support/Vocabulary/VocabularyContentPolicy.swift`: reference metadata limits | Existing Core policy; native validation additionally checks exact bytes/hash, PNG signature/container and decoded dimensions |
| `CotabbyTests/Services/Vocabulary/VocabularyContentTests.swift`: import bounds and invalid bytes | Related source regression; new corpus executes the complete original ImageIO prepare/validate methods on fixed inputs |

Input is capped at 24 MiB, source width/height at 30,000 and area at 100 million pixels. Only frame
zero is read. The maximum output dimension is 1,600; output is PNG capped at 4 MiB. A prepared
reference hashes the actual returned PNG with lowercase SHA-256 and carries exact dimensions and
byte count. Decoder/codec errors cross the boundary as fixed invalid-content failures. Cancellation
is checked before native admission, between native stages, per bounded rotation row and before
return. A synchronous codec call itself is not interruptible.

The Windows adapter uses WIC directly to request frame zero. WPF's `Frames` collection would create
per-frame wrappers for the whole input even under delayed loading. A synchronous worker owns all
COM references and its apartment lease, releasing them in reverse order before output encoding.
Interop signatures/slots follow the [Microsoft SDK header](https://github.com/microsoft/win32metadata/blob/2f767671802301cf043c717d66bf004ed7863487/generation/WinSDK/RecompiledIdlHeaders/um/wincodec.h).
No native pointer or borrowed input buffer escapes that owner.

Scaling and pixel conversion precede a bounded managed EXIF orientation pass. This avoids the
[documented unbuffered WIC flip/rotate scanline amplification](https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nn-wincodec-iwicbitmapfliprotator).
Where a source color context exists, WIC transforms its primary context into sRGB before source
metadata is discarded; unsupported combinations fail instead of silently treating profiled pixels
as unprofiled. A new pixel-only frame supplies the encoder, retaining no source filename, description
or EXIF. The platform PNG encoders need not emit identical bytes or hashes. Wide-gamut/CMYK/profile
fidelity and optional Windows codecs still need broader comparisons with ImageIO; this library
must not be described as full image-format/color parity.

## Evidence and remaining work

`vocabulary-image-preparation-v1.json` contains ten original Mac observations. Identical PNG bytes
exercise small/landscape/portrait/odd-ratio/thin images, 30,001-pixel rejection, empty/junk input,
a length-only input-budget probe, and a CRC-correct oversized-area header. That last input is an
invalid pixel payload as well; the shared observation establishes rejection, not successful decoding
or a precise internal rejection stage. The complete original methods also validate their returned
PNG. Expected values compare dimensions/rejection and PNG/hash/count/budget self-consistency rather
than assuming identical encoder output. The twenty-second Swift verifier executes this corpus.

Windows adds 26 WPF-assembly native cases: ten shared observations, eight actual colored TIFF EXIF
orientations, a 129-frame TIFF on an MTA worker, four exact-reference failures, cancellation, an
embedded sRGB profile and high-entropy output-size rejection. They use real WIC and PNG encoding,
without files, models or a notebook write. These are native adapter tests in the WPF test assembly,
not UI interaction tests or Mac pixel comparisons. The WPF assembly totals 138 cases. One portable
Core test checks detached draft-byte ownership (2883 Core cases). macOS compiles the full solution;
Windows CI executes native cases. ICC/CMYK/wide-gamut equivalence, optional codecs and large rotated
source latency remain verification gaps.

File reads, private media directories/ACLs, staged writes/repair/rollback/cleanup, archive media
budgets, backup/restore, visible-image loading and editor file/paste/drop integration remain the next
boundaries. No source path is persisted, no new provider/transmission is added, and no installer,
downloadable artifact or release is produced.
