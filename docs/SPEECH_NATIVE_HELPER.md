# Isolated native speech helper

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`native/tts/SpeechHelper.cpp`, original TTS build configuration/preparation script, exact
`patches/llama-tts-streaming.patch` and the original native notices. The independent dependency
remains llama.cpp `2a667032dc788c3a071d538e0b572e45a3f12731`.

The helper keeps speech's evolving libmtmd ABI out of the application's independently pinned
LLamaSharp autocomplete/learning libraries. One process owns one model pair/context and sequential
utterances; the runtime from [SPEECH_LOCAL_RUNTIME.md](SPEECH_LOCAL_RUNTIME.md) owns its pipes,
cancellation and idle release. The helper performs no model download or endpoint request. This
phase builds static CPU code; GPU acceleration, application copy/activation and model-backed/audio
acceptance remain work. There is no installer, upload or downloadable package target.

## Original generation and explicit platform seam

`SpeechHelper.cpp` retains the complete original model loading, language inventory, sampler
validation, RNG reset, reference bounds, native generation, cumulative-to-new-PCM slicing, frame
limits, request loop and fixed failure envelopes. `macos_native_speech_reference.py` enumerates
all allowed source transformations and the verifier compares the entire resulting file, plus exact
revision/overlay/license bytes. Changes outside that list fail the67th Mac source check. This is a
source correspondence guard; it does not establish model quality or exact generated PCM across CPUs.

`SpeechPlatform.h` separates only OS effects. Windows uses `wmain` and strict UTF-16→UTF-8 argv,
`filesystem::u8path`/`u8string` for native and upstream UTF-8 paths, CRT binary stdin/stdout and
bounded `_write` calls. Text-mode newline/0x1a handling must never change framed PCM. Both model
filenames and the reference filename reach their respective libraries in the required encoding.
ASCII extension lowering uses an unsigned character to avoid undefined behavior on non-ASCII bytes.
Windows parent IDs retain the DWORD range; malformed/overflow IDs fail the fixed startup envelope.

The parent watcher checks the process creator through
[PROCESSENTRY32W](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32w),
retains a synchronize/query handle and rejects a reused PID whose
[creation time](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes)
is later than this helper. A detached thread blocks on that exact kernel object and exits the helper
when it dies. The runtime's atomic Job Object assignment protects creation as well; the watcher is
independent defense and is not a polling loop.

Reference admission still checks WAV/MP3, regular-file status and10 MiB before decoding. Windows
uses a local COM/Media Foundation scope and source-reader
[MF_PD_DURATION metadata](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader)
to enforce positive duration at most30 seconds before asking libmtmd to allocate its reference
waveform. It does not read PCM samples in this admission step. COM/Media Foundation initialization,
reader interfaces and variants are released on every branch. Metadata/backend availability can
differ from AudioToolbox, including Windows editions without media components; those failures stay
explicit and no codec/component is automatically downloaded.

`SpeechPlatformMac.cpp` supplies original POSIX/kqueue/AudioToolbox effects solely for compiling
and checking this port in the developer's Mac checkout. It does not modify the pinned Mac repository
or establish Windows-native verification. The actual Windows helper links its Windows adapter.

## Reproducible dependency and checks

`prepare-speech-runtime.py` initializes/fetches only the exact source pin and applies only the exact
reviewed six-file overlay (SHA256 `723e6747f2f3734f3b64c669bf5b26a368e1fe798cc85878d5580547f90fae47`).
Existing staged/untracked/unknown tracked changes and a wrong revision are preserved and rejected.
Git diff rendering explicitly uses the original nine-digit blob abbreviations, fixed prefixes and
Myers context; automatic abbreviation varies between a shallow CI clone and a developer object
store. A second preparation is idempotent. Direct CMake configuration also invokes read-only verification
so an unprepared or different dependency cannot be mislabeled by the fixed version frame. Patch bytes are not rewritten for host line endings.
Eight real-Git preparation tests exercise both success/idempotence and preserved unsafe states.

The CMake project disables shared/dynamic backends, server/network support and unrelated tools in
its selected target; it builds the real helper independently of .NET. The original streaming/RNG
patch remains unchanged. Notices and MIT source attribution are retained alongside the repository's
AGPL license. A separate test target exercises only native path, parent and reference effects.

Model-free binary checks validate exact framed version/fatal JSON, bounded single-frame output,
private-input exclusion from failures, Unicode GGUF-path rejection, UTF-8 roundtrip, PID boundaries,
WAV0/1/30/31-second metadata and invalid audio. Windows additionally runs an abrupt-parent fixture:
it retains the exact child process handle, waits for its watcher barrier, exits its creator without
Popen cleanup, and confirms the helper exits. Its standby loop ignores stdin EOF, so pipe closure
cannot satisfy the test. Cleanup terminates/joins any test-owned survivor and closes handles.

Local Mac CPU compilation and13 actual binary/platform checks passed without weights. The linker
reported an upstream duplicate static llama library warning. Windows MSVC compilation,14 actual
binary/platform checks and8 Git preparation checks are CI gates. Native PCM continuity with models,
MP3/other-container behavior, GPU performance, actual reference cloning, audible playback and the
physical app shutdown path still require further evidence.

## Visual Studio source ownership

The pinned upstream uses unity translation units for model sources while compiling its core
`src/llama.cpp` separately. MSBuild also inventories the excluded `src/models/llama.cpp` member;
its shared default basename produced MSB8027 in the first Windows build. The logs alone did not
establish an actual object overwrite. The port assigns the model inventory item a distinct
`ObjectFileName` using source-scoped [CMake VS_SETTINGS](https://cmake.org/cmake/help/v3.24/prop_sf/VS_SETTINGS.html)
in the existing `llama` target's directory. It does not rename code, change unity membership,
exclude an additional compiler input or edit the verified dependency overlay.

Before compiling, `check-speech-vs-project.py` reads the actual generated `llama.vcxproj` and unity
files. For every generated configuration it checks that core remains directly compiled, the model
member is excluded from direct compilation with its distinct object metadata, and exactly one
compiled unity unit owns that model without containing core. It prints only structural evidence.
The Windows MSBuild invocation promotes **MSB8027 specifically** to an error, retaining unrelated
upstream compiler diagnostics rather than suppressing the collision warning. See
[Microsoft's diagnostic](https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb8027?view=visualstudio)
and [warning-code policy](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference?view=visualstudio).

Local synthetic XML admission/rejection checks validate this parser, and Mac CMake configuration
checks the unchanged non-Visual-Studio path. Only Windows CI can establish the generated Visual
Studio metadata and subsequent native compilation. This additional build gate still uses no model
weights, downloads no package output and does not establish model-backed inference or playback.
