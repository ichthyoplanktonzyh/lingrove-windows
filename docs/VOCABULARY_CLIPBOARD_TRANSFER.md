# Bounded image clipboard transfer library

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`,
`Cotabby/UI/Vocabulary/VocabularyContentEditor.swift` and
`Cotabby/Services/Vocabulary/VocabularyImageTransfer.swift`.

The Mac editor's dedicated image paste tries PNG, TIFF, then JPEG data. A nil representation can
try the next format; a non-nil empty or invalid representation proceeds to preparation and fails
there, without trying a different image. Its provider bridge also releases the waiting task on
cancellation even if the foreign provider never calls back. These are the behavior references.

This slice initially supplied the Windows extraction library and its ordinary build dependency.
The [image paste integration](VOCABULARY_IMAGE_PASTE.md) now connects the button and explanation
section command through one AppHost owner. The [DIB adapter](VOCABULARY_CLIPBOARD_DIB.md) now adds
bounded Windows bitmap envelopes after the encoded groups. Clipboard text, virtual files and
pixel-only drops remain outside this library's implemented formats.

## Ownership and data flow

`WindowsVocabularyClipboardTransfer` is intended to be owned once by AppHost. It admits at most
one worker and one direct child, declining concurrent calls synchronously. A read receives the
clipboard sequence captured at user intent, launches only the fixed sibling executable with that
numeric argument, and returns detached bytes or no available image. No input path, shell command,
typed text or native handle crosses the process boundary. Callers must subsequently pass bytes
through the existing archive preparation/validation boundary before adding a draft block.

`Cotabby.ImageTransferHost` is a small STA console program with no WPF, inference or model
dependency. Its only valid operation reads encoded clipboard images. It compiles the same
`VocabularyClipboardImageReader`, `VocabularyImageTransferProtocol` and native declarations as
Win32. Four runtime sidecars are copied by the ordinary App project build and transitively into
the WPF test output. This adds no installer, upload, release job or downloaded model.

The reader opens the clipboard read-only with three bounded acquisition attempts. It checks the
captured sequence before and after acquiring the open, then requests fixed HGLOBAL representations:
registered `PNG`/`image/png`, standard `CF_TIFF`, registered `JFIF`/`JPEG`/`image/jpeg`, then
standard `CF_DIBV5`/`CF_DIB` (detached/validated into BMP by the linked DIB adapter).
Registered names are explicit producer conventions; no automatic format conversion is requested.
Only the first non-null handle is copied, including an empty allocation. Its global size is
bounded to 24 MiB before allocation/locking. Clipboard owner and sequence must stay stable through
the copy, with a sequence advance during delayed rendering allowed before copying. Unlock and
Close run in `finally`. The host never empties/replaces the clipboard or frees OS-owned handles.

Stdout contains one little-endian `LGC1` header and signed byte count, then exactly those bytes:
`-1` is unavailable, `-2` is failure, and `0` is a non-null empty representation. The parent rejects
bad magic, invalid/oversized lengths, truncation, trailing bytes and a nonzero child exit. Stderr
is discarded with a 32 KiB cap. Neither pipe is logged. Malformed replies and timeouts become the
fixed vocabulary content failure; caller/app cancellation stays cancellation.

Win32 [`GetClipboardData`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata)
can invoke a foreign delayed renderer synchronously. Moving it to an in-process thread alone
would leave that thread blocked after the editor cancels. The parent instead links caller and
app lifetime cancellation with a 30 second deadline, terminates its own child in `finally` and
bounds the exit wait to two seconds. Disposal closes admission, cancels and drains admitted work.
Only the fixed helper is started, and it starts no descendants. This is lifetime/fault isolation,
not a reduced-privilege security sandbox or a guarantee against OS process-termination failure.

Microsoft documents [delayed rendering and clipboard ownership](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations),
the [sequence behavior](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber),
[registered formats](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerclipboardformatw)
and [standard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats).

## Validation boundary

Thirty-six reader/protocol cases use app-owned unmanaged memory, scripted native calls and
independent literal wire frames. They verify representation ordering, nil versus invalid/empty
data, busy/stale opens, size-before-lock, failed lock cleanup, delayed-render sequence admission,
owner/copy races, metadata-only admission, DIB selection/close ordering and malformed framing. They never open or change the runner clipboard.

Twelve process cases launch actual installed Windows PowerShell fixture processes with fixed
test bytes and private PID markers. They verify data/empty/unavailable replies, fixed failures,
stderr bounds, cancellation of a blocked child, its observed exit, synchronous rejection of a
500-call burst, reuse, disposal, timeout and already-cancelled admission. The scripts never invoke
clipboard APIs. One WPF output case checks all four real helper sidecars and launches the actual
helper with invalid arguments, proving startup/exit before any clipboard access.

The stderr-budget process case observes a PID marker before releasing its fixed 40,000-byte
write. It then requires the content failure and observed child exit within 12 seconds, separately
from the startup bound and before its 60-second fallback deadline. A marker after the write would
be unreliable because correct budget enforcement can terminate the child during that write.

These tests do not establish actual foreign producer compatibility, delayed-render interoperability
or actual PNG/TIFF/JPEG/DIB availability from Paint, browsers or Office. The DIB adapter has
separate structure and WIC pixel cases, with compressed/profiled variants explicitly unsupported. Native clipboard producer checks remain a Windows desktop gate.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
# Native Windows CI; no clipboard mutation:
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyClipboard
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --filter FullyQualifiedName~VocabularyImageTransferHostTests
```
