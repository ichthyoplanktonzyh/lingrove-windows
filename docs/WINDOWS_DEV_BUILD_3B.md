# Runnable Windows development build

Reference: macOS `3b53402f2cbc196b0398d133177fd80006d165b2`,
`scripts/prepare_tts_runtime.sh`, `scripts/embed_tts_runtime.sh` and
`native/tts/CMakeLists.txt`. The Mac build prepares the independent pinned speech process and
embeds it under the application's Helpers directory. Windows now supplies the corresponding
explicit developer workflow in `scripts/build-windows-dev.ps1`; this is an unpackaged local build,
not an installer, downloadable artifact or release.

## Build and ownership

The default command builds the WPF app and its CLI/image-transfer sidecars through their existing
project references. `-IncludeLocalSpeech` additionally prepares the unchanged speech dependency
pin/overlay, configures Visual Studio 2022 x64, checks actual generated source ownership, compiles
the isolated CPU helper and its platform-check executable, and runs the existing model-free binary
checks. Only a successful checked helper and the original revision/license/notices are copied into
`Cotabby.App/bin/<Configuration>/net10.0-windows10.0.19041.0/win-x64/Helpers`. The app-local binary's
SHA-256 must equal the checked build. The platform-check executable and dependency checkout are
not copied into the app.

The helper remains a separate process: autocomplete and learning retain their existing LLamaSharp
libraries; no native speech library is loaded into their runtime. The existing LocalSpeechRuntime
launches the helper lazily at its existing app-relative path. Building/embedding does not launch
the product or enumerate providers, open an audio device, synthesize user text, read a notebook,
select models or change settings. There is no GGUF download, endpoint call, tool installation,
installer generation, artifact upload or publication.

Every external command has an explicit exit-code check. Preparation retains its original refusal
of wrong revisions and unknown dependency edits. An installed Python executable can be supplied
directly; the script excludes WindowsApps aliases and does not invoke an install-manager launcher.
CMake can be supplied directly, discovered on PATH, or located in a VS 2022 C++ installation.
The script changes telemetry only in its own process and restores the previous value.

## Commands

Run from a Windows x64 PowerShell process with the SDK selected by global.json. For the managed
app alone:

```powershell
.\scripts\build-windows-dev.ps1
```

For local speech, use installed Visual Studio 2022 C++/Windows SDK/CMake and Python 3.11+.
A short absolute cache path on a roomy drive avoids filling the checkout's system drive:

```powershell
.\scripts\build-windows-dev.ps1 -IncludeLocalSpeech `
  -NativeBuildRoot D:\DevCache\lingrove-speech `
  -PythonExecutable C:\Tools\Python312\python.exe
```

Replace the example paths with existing host locations. The cache's default is the ignored
`build/speech-native` under this checkout. `-CMakeExecutable`, `-Configuration Debug|Release` and
`-NativeJobs` are optional. No cache deletion or cleanup is performed. Close an already running
development app before rebuilding; locked files cause a failed build/copy rather than a success
receipt. A default managed-only build does not rebuild or remove an existing embedded helper.

The script prints the absolute `Cotabby.exe` path after success. Launch that executable when
ready for the consolidated acceptance round. A managed-only build needs the .NET 10 Windows
Desktop runtime; the native helper also needs the host's MSVC/OpenMP runtime components. This
workflow uses the developer machine's installed toolchain/runtime and does not distribute them.

## Validation and remaining acceptance

On Windows x64 10.0.19045 with SDK 10.0.403, the default script built the app and sidecars with
zero warnings/errors. Actual VS 2022/MSVC 19.44 and SDK 10.0.26100 native CPU compilation also
succeeded, with the unchanged upstream compiler warnings retained. Generated Visual Studio
source ownership passed its checker. The dependency cache was placed on D:, preserving C: space.
Independent source review found and resolved PATH discovery hiding a real Python behind a
WindowsApps alias (`Get-Command -All` now precedes alias exclusion); final review had no new findings.

A fresh independent executor ran `build-windows-dev.ps1 -IncludeLocalSpeech` once against that
prepared D: cache with an explicit installed Python 3.12.14. It exited 0 in 12.4 seconds: managed
build 0 warnings/errors, incremental native build succeeded and all 14 model-free checks passed.
CMake retained its nonfatal missing-ccache notice. The app executable and all three helper notices
were present. The 5,894,144-byte embedded helper matched the native output's SHA-256:
`4FD3F31662D7707259CCFA5321140725CEA2A5AAE165212790B6D331529B1994`.
This is local build evidence, not a checksum promise for other toolchains or revisions.

The existing native checks cover version/fatal framing, bounded output, Unicode paths, invalid model rejection,
reference duration boundaries and exact-parent death without model weights or audible playback.
They do not establish model-backed speech, GPU acceleration, reference cloning quality or audio
hardware. CPU performance, real installed/configured models, endpoint use and audible playback
remain part of the maintainer's final acceptance round alongside Word, Chrome and Codex.

This build workflow closes the missing local helper placement in the development app. It does
not close automatic language-confidence limitations, Apple's unavailable system dictionary,
the separately recorded media/provider gaps or physical desktop/visual acceptance.
